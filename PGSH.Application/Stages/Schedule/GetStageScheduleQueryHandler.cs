using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Extensions;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule;

internal sealed class GetStageScheduleQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ServiceOccupancyCalculator occupancyCalculator,
    ServiceIntakeCalculator intakeCalculator)
    : IQueryHandler<GetStageScheduleQuery, StageScheduleResponse>
{
    /// <summary>
    /// How many saturated (créneau × service) pairs the response lists one by one. The count beside
    /// it stays exact — a bounded list must never be the only thing saying how big the problem is.
    /// </summary>
    private const int MaxReportedSaturations = 100;

    public async Task<Result<StageScheduleResponse>> Handle(
        GetStageScheduleQuery request, CancellationToken cancellationToken)
    {
        // Both axes of the grid are year-scoped: slots carry the year's dates, and a cohort exists
        // per (stage, group) with groups per year — unscoped, "Chirurgie" returned the 681 cohorts of
        // every year it ever ran.
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<StageScheduleResponse>(year.Error);

        int academicYearId = year.Value;

        // The grid is one stage, so one level: every quota shown below is that level's.
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.LevelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (stage is null)
            return Result.Failure<StageScheduleResponse>(StageErrors.NotFound(request.StageId));

        int levelId = stage.LevelId;

        var slots = await SlotsQuery(dbContext, request.StageId, academicYearId)
            .ToListAsync(cancellationToken);

        var scope = ScopedCohortsQuery(dbContext, request.StageId, academicYearId, request.RotationGroup);

        // Every (créneau × service) the selection puts somebody in — a fact about the pair, not about
        // each cohorte standing in it. Bounded by columns × services whatever the promotion's size,
        // which is what lets the saturation report stay whole while the rows are paged.
        var pairs = await ScopedCellPairsQuery(dbContext, request.StageId, academicYearId, request.RotationGroup)
            .ToListAsync(cancellationToken);

        var serviceIds = pairs.Select(p => p.ServiceId).Distinct().ToList();

        var occupancy = await occupancyCalculator.BuildAsync(serviceIds, cancellationToken);
        var intake = await intakeCalculator.BuildAsync(serviceIds, cancellationToken);

        var page = await scope.ToPaginatedResponseAsync(
            request.EffectivePageNumber,
            request.EffectivePageSize,
            c => new CohortRow(
                c.Id,
                c.Label,
                c.AcademicGroupId,
                c.AcademicGroup.Label,
                c.AcademicGroup.RotationGroup,
                c.Assignments.Count,
                c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null))),
            cancellationToken);

        var cellsByCohort = (await PageCellsQuery(dbContext, page.Items.Select(c => c.Id).ToList())
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.CohortId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.StageSlotId));

        var slotById = slots.ToDictionary(s => s.Id);

        var rows = page.Items.Select(c =>
        {
            var cells = cellsByCohort.GetValueOrDefault(c.Id) ?? [];

            return new CohortScheduleRow(
                c.Id, c.Label, c.AcademicGroupId, c.AcademicGroupLabel, c.RotationGroup,
                c.StudentCount, c.IsSchedulePublished,
                slots.Select(slot => cells.TryGetValue(slot.Id, out var cell)
                    ? CellFor(cell, slot, levelId, intake, occupancy)
                    : null).ToList());
        }).ToList();

        var summary = await BuildSummaryAsync(
            request, academicYearId, levelId, scope, pairs, slotById, intake, occupancy, cancellationToken);

        return new StageScheduleResponse(
            request.StageId,
            slots,
            new PaginatedResponse<CohortScheduleRow>(rows, page.PageNumber, page.PageSize, page.TotalCount),
            summary);
    }

    private async Task<StageScheduleSummary> BuildSummaryAsync(
        GetStageScheduleQuery request,
        int academicYearId,
        int levelId,
        IQueryable<Cohort> scope,
        IReadOnlyList<CellPair> pairs,
        IReadOnlyDictionary<int, StageSlotResponse> slotById,
        ServiceIntakeLookup intake,
        ServiceOccupancyLookup occupancy,
        CancellationToken ct)
    {
        // ⚠ Measured over the whole selection, never over the page. The publish button beside these
        // numbers acts on the selection, so a count taken from 25 visible rows would have promised to
        // publish 25 of the 90 cohortes it was about to publish.
        var states = await scope
            .Select(c => new
            {
                IsPublished = c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)),
                HasCells    = c.SlotAssignments.Any(),
            })
            .ToListAsync(ct);

        // The partitions of the stage, not of the current filter: they are what the user filters
        // *with*, so narrowing them by the active filter would leave no way back to the others.
        var partitions = await PartitionsQuery(dbContext, request.StageId, academicYearId)
            .ToListAsync(ct);

        // Which partition stands in which column, over the whole stage — read unfiltered on purpose:
        // « la partition A est-elle seule sur P4-P6 ? » is a question about the partitions the filter
        // has just removed, so a client holding only the filtered rows can never answer it.
        var partitionUsage = await PartitionSlotUseQuery(dbContext, request.StageId, academicYearId)
            .ToListAsync(ct);

        var saturated = pairs
            .Select(pair => Saturation(pair, slotById[pair.StageSlotId], levelId, intake, occupancy))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.Reason == SaturationReason.Refused)
            .ThenByDescending(s => s.OccupiedSeats - s.Capacity)
            .ToList();

        return new StageScheduleSummary(
            states.Count,
            states.Count(s => s.IsPublished),
            states.Count(s => !s.IsPublished && s.HasCells),
            partitions.OrderBy(p => p.Label).ToList(),
            saturated.Count,
            saturated.Take(MaxReportedSaturations).ToList(),
            // Which columns the *selection* already occupies — what separates « ajouter une colonne
            // et ne répartir qu'elle » from rewriting a rotation that is already correct.
            pairs.Select(p => p.StageSlotId).Distinct().Order().ToList(),
            partitionUsage);
    }

    /// <summary>The one limit that governs a cell, and the load counted the way that limit is written.</summary>
    private static SlotCellResponse CellFor(
        CellDetail cell, StageSlotResponse slot, int levelId,
        ServiceIntakeLookup intake, ServiceOccupancyLookup occupancy)
    {
        var (capacity, occupied, isLevelQuota) = Limit(cell.ServiceId, slot, levelId, intake, occupancy);

        return new SlotCellResponse(
            cell.Id, cell.StageSlotId, cell.ServiceId, cell.ServiceName, cell.HospitalName,
            capacity, occupied, isLevelQuota, intake.Admits(cell.ServiceId, levelId));
    }

    private static SaturatedCellResponse? Saturation(
        CellPair pair, StageSlotResponse slot, int levelId,
        ServiceIntakeLookup intake, ServiceOccupancyLookup occupancy)
    {
        bool admits = intake.Admits(pair.ServiceId, levelId);
        var (capacity, occupied, isLevelQuota) = Limit(pair.ServiceId, slot, levelId, intake, occupancy);

        if (admits && occupied <= capacity)
            return null;

        return new SaturatedCellResponse(
            pair.StageSlotId, slot.PeriodNumber, pair.ServiceId, pair.ServiceName, pair.HospitalName,
            occupied,
            // A service that does not take the promotion has no capacity for it to be under: naming
            // its total here would read as "there is room", which is the opposite of the refusal.
            admits ? capacity : 0,
            !admits ? SaturationReason.Refused
                : isLevelQuota ? SaturationReason.Quota
                : SaturationReason.Total);
    }

    /// <summary>
    /// The load has to be counted the same way the governing limit is written: against this promotion
    /// when the service grants it a quota, against everybody when the service has one number for all
    /// comers. Read once here so a cell and the saturation report can never disagree about it.
    /// </summary>
    private static (int Capacity, int Occupied, bool IsLevelQuota) Limit(
        int serviceId, StageSlotResponse slot, int levelId,
        ServiceIntakeLookup intake, ServiceOccupancyLookup occupancy)
    {
        bool isLevelQuota = intake.HasLevelRestrictions(serviceId);

        int occupied = isLevelQuota
            ? occupancy.LoadOn(serviceId, levelId, slot.StartDate, slot.EndDate)
            : occupancy.LoadOn(serviceId, slot.StartDate, slot.EndDate);

        return (intake.CapacityFor(serviceId, levelId), occupied, isLevelQuota);
    }

    /// <summary>The columns of the axis — bounded by T, so never paged.</summary>
    internal static IQueryable<StageSlotResponse> SlotsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == stageId && s.AcademicYearId == academicYearId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new StageSlotResponse(s.Id, s.PeriodNumber, s.Label, s.StartDate, s.EndDate));

    /// <summary>
    /// The cohortes the grid is showing: one stage, one year, optionally one partition. Ordered by
    /// roster number so a page boundary falls somewhere a reader can predict, and tie-broken on the
    /// id — a page taken from an unstable order can show one row twice and never show another.
    /// </summary>
    internal static IQueryable<Cohort> ScopedCohortsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId, string? rotationGroup)
    {
        var query = dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId);

        if (!string.IsNullOrWhiteSpace(rotationGroup))
            query = query.Where(c => c.AcademicGroup.RotationGroup == rotationGroup);

        return query
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .ThenBy(c => c.Id);
    }

    /// <summary>
    /// The distinct (créneau, service) pairs the selection occupies — what the saturation report is
    /// made of. Bounded by columns × services, so it stays whole however many cohortes there are.
    /// </summary>
    /// <remarks>
    /// ⚠ Named so <c>SqlTranslationTests</c> can compile it: a <c>Distinct</c> over a computed
    /// element is the family Npgsql refused on the macro-plan path, and only compiling it says which
    /// side of that line a projection into a record falls on.
    /// </remarks>
    internal static IQueryable<CellPair> ScopedCellPairsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId, string? rotationGroup)
    {
        var query = dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.StageId == stageId
                     && a.Cohort.AcademicGroup.AcademicYearId == academicYearId);

        if (!string.IsNullOrWhiteSpace(rotationGroup))
            query = query.Where(a => a.Cohort.AcademicGroup.RotationGroup == rotationGroup);

        return query
            .Select(a => new CellPair(a.StageSlotId, a.ServiceId, a.Service.Name, a.Service.Hospital.Name))
            .Distinct();
    }

    /// <summary>The cells of the cohortes on this page, and nothing else.</summary>
    internal static IQueryable<CellDetail> PageCellsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CohortId))
            .Select(a => new CellDetail(
                a.Id, a.CohortId, a.StageSlotId, a.ServiceId, a.Service.Name, a.Service.Hospital.Name));

    /// <summary>
    /// Which partitions this stage's cohortes carry, with how many each holds. Computed where the
    /// rows are: derived from a page it would under-report the moment a promotion outgrew it — the
    /// defect the Plan macro tab had at <c>pageSize: 200</c>.
    /// </summary>
    internal static IQueryable<PartitionSummary> PartitionsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId
                     && c.AcademicGroup.AcademicYearId == academicYearId
                     && c.AcademicGroup.RotationGroup != null)
            .GroupBy(c => c.AcademicGroup.RotationGroup!)
            .Select(g => new PartitionSummary(g.Key, g.Count()));

    /// <summary>
    /// Which partition occupies which column of this stage. Never narrowed by the caller's partition
    /// filter — see <see cref="PartitionSlotUse"/> for why the answer has to include the partitions
    /// the filter removed.
    /// </summary>
    internal static IQueryable<PartitionSlotUse> PartitionSlotUseQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.StageId == stageId
                     && a.Cohort.AcademicGroup.AcademicYearId == academicYearId)
            .Select(a => new PartitionSlotUse(a.Cohort.AcademicGroup.RotationGroup, a.StageSlotId))
            .Distinct();

    /// <summary>One cohorte of the page, before its cells are folded in.</summary>
    internal sealed record CohortRow(
        int Id, string Label, int AcademicGroupId, string AcademicGroupLabel,
        string? RotationGroup, int StudentCount, bool IsSchedulePublished);

    /// <summary>One cell of a cohorte on the page.</summary>
    internal sealed record CellDetail(
        int Id, int CohortId, int StageSlotId, int ServiceId, string ServiceName, string HospitalName);

    /// <summary>One (créneau, service) the selection occupies, whoever is standing in it.</summary>
    internal sealed record CellPair(int StageSlotId, int ServiceId, string ServiceName, string HospitalName);
}
