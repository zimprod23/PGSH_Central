using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Extensions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Placements;

internal sealed class GetRosterPlacementsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetRosterPlacementsQuery, RosterPlacementsResponse>
{
    public async Task<Result<RosterPlacementsResponse>> Handle(
        GetRosterPlacementsQuery request, CancellationToken cancellationToken)
    {
        // Rosters are year-constituted and cells hang off them, so the year is not optional here. An
        // omitted one is the current one, never all of them: unscoped, a promotion returns every year
        // it ever ran and « ce groupe va au HMIMV » would be true of a roster dissolved in 2019.
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<RosterPlacementsResponse>(year.Error);

        int academicYearId = year.Value;

        // ⚠ The level's existence is checked rather than left to return an empty page. This read
        // exists precisely to tell « personne n'y va » from « rien n'est encore réparti »; a typo'd
        // levelId silently answering zero would put a third meaning behind the same blank, which is
        // the ambiguity the whole feature removes. Its sibling GetPromotionPartitioningQuery does not
        // check, and that is the weaker behaviour, not the pattern to copy.
        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<RosterPlacementsResponse>(LevelErrors.NotFound(request.LevelId));

        var target = new PlacementTarget(request.ServiceId, request.HospitalId);

        var matching = MatchingRostersQuery(
            dbContext, academicYearId, request.LevelId,
            request.StageId, request.ServiceId, request.HospitalId, request.Match);

        var page = await matching.ToPaginatedResponseAsync(
            request.EffectivePageNumber,
            request.EffectivePageSize,
            g => new RosterRow(g.Id, g.Label, g.GroupNumber, g.RotationGroup, g.Registrations.Count),
            cancellationToken);

        var groupIds = page.Items.Select(r => r.GroupId).ToList();

        // Two flat reads keyed on the page's roster ids, never collections folded into the row
        // projection: the element of such a subquery is a computed value carrying no key, which is
        // the shape Npgsql refuses and the one that killed the macro plan.
        var stageRows = await PageStagesQuery(dbContext, groupIds, request.StageId)
            .ToListAsync(cancellationToken);

        var cellRows = await PageCellsQuery(dbContext, groupIds, request.StageId)
            .ToListAsync(cancellationToken);

        var stagesByGroup = stageRows
            .GroupBy(s => s.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StageName, StringComparer.CurrentCulture).ToList());

        var cellsByGroup = cellRows
            .GroupBy(c => c.GroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rosters = page.Items
            .Select(r => Describe(r, stagesByGroup, cellsByGroup, target, request.Match, request.HasTarget))
            .ToList();

        var summary = await BuildSummaryAsync(
            academicYearId, request.LevelId, page.TotalCount, cancellationToken);

        return new RosterPlacementsResponse(
            academicYearId,
            request.LevelId,
            new PaginatedResponse<RosterPlacementResponse>(
                rosters, page.PageNumber, page.PageSize, page.TotalCount),
            summary);
    }

    private static RosterPlacementResponse Describe(
        RosterRow roster,
        IReadOnlyDictionary<int, List<StageRow>> stagesByGroup,
        IReadOnlyDictionary<int, List<CellRow>> cellsByGroup,
        PlacementTarget target,
        PlacementMatch match,
        bool hasTarget)
    {
        var stages = stagesByGroup.GetValueOrDefault(roster.GroupId) ?? [];
        var cells = cellsByGroup.GetValueOrDefault(roster.GroupId) ?? [];
        var cellsByStage = cells.GroupBy(c => c.StageId).ToDictionary(g => g.Key, g => g.ToList());

        var placements = stages
            .Select(stage =>
            {
                var stageCells = cellsByStage.GetValueOrDefault(stage.StageId) ?? [];

                return new RosterStagePlacementResponse(
                    stage.StageId,
                    stage.StageName,
                    hasTarget ? Satisfies(stageCells, target, match) : null,
                    ServicesOf(stageCells));
            })
            .ToList();

        return new RosterPlacementResponse(
            roster.GroupId,
            roster.Label,
            roster.GroupNumber,
            roster.RotationGroup,
            roster.StudentCount,
            stages.Count,
            placements.Count(p => p.Services.Count > 0),
            placements.Count(p => p.Matches == true),
            target.HospitalId is null
                ? null
                : RosterHospitalPlacementTest.Of(cells.Count, cells.Count(target.Hits)),
            placements);
    }

    /// <summary>
    /// The cells of one stage, folded to one entry per service. Ordered by first créneau so a
    /// <c>PerPeriod</c> rotation reads in the order the student lives it.
    /// </summary>
    private static List<RosterServicePlacementResponse> ServicesOf(IReadOnlyCollection<CellRow> cells) =>
        cells
            .GroupBy(c => new { c.ServiceId, c.ServiceName, c.HospitalId, c.HospitalName })
            .Select(g => new RosterServicePlacementResponse(
                g.Key.ServiceId,
                g.Key.ServiceName,
                g.Key.HospitalId,
                g.Key.HospitalName,
                g.Select(c => c.PeriodNumber).Order().ToList()))
            .OrderBy(s => s.PeriodNumbers.Count > 0 ? s.PeriodNumbers[0] : int.MaxValue)
            .ToList();

    /// <summary>
    /// Whether one stage's cells satisfy the target, under the same rule
    /// <see cref="MatchingRostersQuery"/> applies in SQL to decide which rosters come back at all.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Two statements of one rule, on either side of the network boundary</b> — the risk
    /// <c>ServicePeriodLifecycle</c> removes by compiling its delegates from its expressions. It
    /// cannot be done here: EF needs the comparison written inline inside the nested <c>Any</c>, and
    /// a composed <c>Expression</c> would not translate. What holds them together instead is a test —
    /// every roster the SQL returns must carry at least one matching stage, and a roster it excludes
    /// must carry none. Drift then fails the suite rather than showing a roster with no reason to be
    /// in the list.
    /// </remarks>
    private static bool Satisfies(
        IReadOnlyCollection<CellRow> stageCells, PlacementTarget target, PlacementMatch match)
    {
        int hits = stageCells.Count(target.Hits);

        return match == PlacementMatch.Anywhere
            ? hits > 0
            : stageCells.Count > 0 && hits == stageCells.Count;
    }

    private async Task<RosterPlacementSummary> BuildSummaryAsync(
        int academicYearId, int levelId, int matchedRosters, CancellationToken ct)
    {
        // ⚠ Measured over the promotion, never over the page — and PlacedRosters is the number the
        // screen needs most, because it is the only thing separating « personne n'y va » from
        // « rien n'est encore réparti ». Three cheap aggregates rather than the rows themselves.
        var scope = ScopedRostersQuery(dbContext, academicYearId, levelId);

        int promotionRosters = await scope.CountAsync(ct);

        int placedRosters = await scope
            .CountAsync(g => g.Cohorts.Any(c => c.SlotAssignments.Any()), ct);

        int promotionStages = await PromotionStagesQuery(dbContext, academicYearId, levelId)
            .CountAsync(ct);

        return new RosterPlacementSummary(
            promotionRosters, placedRosters, matchedRosters, promotionStages);
    }

    /// <summary>
    /// The rosters of one promotion.
    /// </summary>
    /// <remarks>
    /// « Non réparti » is excluded by construction rather than by a special case: the bucket belongs
    /// to no promotion and so carries a null <c>LevelId</c>, which no equality against a level can
    /// match. That is the same reason the partitioning read needs no exclusion either.
    /// </remarks>
    internal static IQueryable<AcademicGroup> ScopedRostersQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == academicYearId && g.LevelId == levelId);

    /// <summary>Distinct stages the promotion's rosters hold a cohorte for.</summary>
    internal static IQueryable<int> PromotionStagesQuery(
        IApplicationDbContext dbContext, int academicYearId, int levelId) =>
        dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroup.AcademicYearId == academicYearId
                     && c.AcademicGroup.LevelId == levelId)
            .Select(c => c.StageId)
            .Distinct();

    /// <summary>
    /// The rosters satisfying the placement target, ordered by roster number so a page boundary falls
    /// somewhere a reader can predict, and tie-broken on the id — a page taken from an unstable order
    /// can show one row twice and never show another.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b><see cref="PlacementMatch.Exclusively"/> is two conditions, and the first is the one
    /// that matters.</b> « Aucune cellule ailleurs » is satisfied by a roster with no cell at all, so
    /// the positive half — it holds at least one cell in scope — has to be asserted separately. Left
    /// out, every unarranged roster of the promotion is returned as an exact match, which on this
    /// base (0 cells) is every roster there is.</para>
    /// <para>The service and hospital branches are written out rather than composed: EF needs the
    /// comparison inline inside the nested <c>Any</c>. Applied in sequence, so a caller sending both
    /// gets their conjunction — which is the correct reading, though the validator refuses it.</para>
    /// </remarks>
    internal static IQueryable<AcademicGroup> MatchingRostersQuery(
        IApplicationDbContext dbContext,
        int academicYearId,
        int levelId,
        int? stageId,
        int? serviceId,
        int? hospitalId,
        PlacementMatch match)
    {
        var rosters = ScopedRostersQuery(dbContext, academicYearId, levelId);

        if (stageId is not null)
            rosters = rosters.Where(g => g.Cohorts.Any(c => c.StageId == stageId));

        if (serviceId is not null)
            rosters = match == PlacementMatch.Anywhere
                ? rosters.Where(g => g.Cohorts.Any(c =>
                    (stageId == null || c.StageId == stageId)
                    && c.SlotAssignments.Any(a => a.ServiceId == serviceId)))
                : rosters.Where(g => g.Cohorts.Any(c =>
                        (stageId == null || c.StageId == stageId) && c.SlotAssignments.Any())
                    && !g.Cohorts.Any(c =>
                        (stageId == null || c.StageId == stageId)
                        && c.SlotAssignments.Any(a => a.ServiceId != serviceId)));

        if (hospitalId is not null)
            rosters = match == PlacementMatch.Anywhere
                ? rosters.Where(g => g.Cohorts.Any(c =>
                    (stageId == null || c.StageId == stageId)
                    && c.SlotAssignments.Any(a => a.Service.HospitalId == hospitalId)))
                : rosters.Where(g => g.Cohorts.Any(c =>
                        (stageId == null || c.StageId == stageId) && c.SlotAssignments.Any())
                    && !g.Cohorts.Any(c =>
                        (stageId == null || c.StageId == stageId)
                        && c.SlotAssignments.Any(a => a.Service.HospitalId != hospitalId)));

        return rosters.OrderBy(g => g.GroupNumber).ThenBy(g => g.Id);
    }

    /// <summary>
    /// The stages the rosters on this page hold a cohorte for — including the ones with no cell yet,
    /// which is what lets a stage be reported as « reste à répartir » instead of vanishing.
    /// </summary>
    internal static IQueryable<StageRow> PageStagesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> groupIds, int? stageId) =>
        dbContext.Cohorts
            .AsNoTracking()
            .Where(c => groupIds.Contains(c.AcademicGroupId)
                     && (stageId == null || c.StageId == stageId))
            .Select(c => new StageRow(c.AcademicGroupId, c.StageId, c.Stage.Name));

    /// <summary>The planning cells of the rosters on this page, and nothing else.</summary>
    internal static IQueryable<CellRow> PageCellsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> groupIds, int? stageId) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => groupIds.Contains(a.Cohort.AcademicGroupId)
                     && (stageId == null || a.Cohort.StageId == stageId))
            .Select(a => new CellRow(
                a.Cohort.AcademicGroupId,
                a.Cohort.StageId,
                a.StageSlot.PeriodNumber,
                a.ServiceId,
                a.Service.Name,
                a.Service.HospitalId,
                a.Service.Hospital.Name));

    internal sealed record RosterRow(
        int GroupId, string Label, int GroupNumber, string? RotationGroup, int StudentCount);

    internal sealed record StageRow(int GroupId, int StageId, string StageName);

    internal sealed record CellRow(
        int GroupId, int StageId, int PeriodNumber,
        int ServiceId, string ServiceName, int HospitalId, string HospitalName);

    /// <summary>
    /// What a cell has to be in to count. A service names itself; a hospital names every service it
    /// holds. Naming neither means the caller is browsing the promotion's placements rather than
    /// searching them, and nothing is matched or refused.
    /// </summary>
    private readonly record struct PlacementTarget(int? ServiceId, int? HospitalId)
    {
        public bool Hits(CellRow cell) =>
            (ServiceId is null || cell.ServiceId == ServiceId)
            && (HospitalId is null || cell.HospitalId == HospitalId);
    }
}
