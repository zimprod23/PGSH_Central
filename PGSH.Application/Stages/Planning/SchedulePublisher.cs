using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <param name="SkippedAlreadyServed">
/// Student assignments that already carried a service period and were therefore left alone. These
/// are the stages someone has already done — an imported historical rotation, a délocalisation, a
/// revalidation — and publishing over them would duplicate the stage rather than schedule it.
/// </param>
public sealed record PublishResult(
    int PublishedCohorts, int PeriodsCreated, int SkippedCohorts, int SkippedAlreadyServed = 0);

/// <summary>
/// Materialises the planned schedule into execution records: one
/// <see cref="ServicePeriod"/> per (student assignment × slot assignment).
/// A cohort counts as published once any of its assignments has a service
/// period linked to a slot assignment. Supports the strict per-cohort publish
/// (returns business failures) and a lenient per-stage publish that skips
/// already-published or unconfigured cohorts, optionally scoped to a partition
/// and/or a window of periods.
/// </summary>
internal sealed class SchedulePublisher(
    IApplicationDbContext dbContext,
    ServiceOccupancyCalculator occupancyCalculator,
    ServiceIntakeCalculator intakeCalculator)
{
    public async Task<Result> PublishCohortAsync(int cohortId, bool allowOverCapacity, CancellationToken ct)
    {
        bool cohortExists = await dbContext.Cohorts.AnyAsync(c => c.Id == cohortId, ct);
        if (!cohortExists)
            return Result.Failure(StageErrors.CohortNotFound(cohortId));

        if (await IsPublishedAsync(cohortId, ct))
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        var slotAssignments = await LoadSlotAssignmentsAsync([cohortId], null, ct);
        if (slotAssignments.Count == 0)
            return Result.Failure(StageErrors.ScheduleNotConfigured);

        // ⚠ An assignment that already holds a period has already been served — an imported
        // historical rotation, a délocalisation, a revalidation. Publishing over it would add a
        // second set of periods for the same stage, which the score then averages and the lifecycle
        // then waits on. Publication materialises a plan; it never re-materialises a past.
        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == cohortId && !a.ServicePeriods.Any())
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (assignmentIds.Count == 0)
            return Result.Failure(StageErrors.NoPlannedAssignments);

        if (!allowOverCapacity)
        {
            var capacity = await EnsureCapacityAsync(slotAssignments, ct);
            if (capacity.IsFailure)
                return capacity;
        }

        var periods = BuildPeriods(slotAssignments, assignmentIds);
        await dbContext.ServicePeriods.AddRangeAsync(periods, ct);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PublishResult>> PublishStageAsync(
        int stageId,
        int academicYearId,
        IReadOnlyCollection<string>? partitionLabels,
        IReadOnlyCollection<int>? periodNumbers,
        bool allowOverCapacity,
        CancellationToken ct)
    {
        var cohortQuery = dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId);

        if (partitionLabels is { Count: > 0 })
            cohortQuery = cohortQuery.Where(c => c.AcademicGroup.RotationGroup != null
                                              && partitionLabels.Contains(c.AcademicGroup.RotationGroup));

        var cohortIds = await cohortQuery.Select(c => c.Id).ToListAsync(ct);
        if (cohortIds.Count == 0)
            return Result.Success(new PublishResult(0, 0, 0));

        var publishedCohortIds = (await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId)
                     && a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null))
            .Select(a => a.CurrentCohortId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        // Already-served assignments are excluded, not skipped as whole cohorts: a cohort routinely
        // mixes students who have the stage behind them (repeaters, délocalisés) with students who
        // do not, and the latter still need their schedule. See PublishCohortAsync for why.
        var candidates = await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => new { a.Id, a.CurrentCohortId, AlreadyServed = a.ServicePeriods.Any() })
            .ToListAsync(ct);

        int skippedAlreadyServed = candidates.Count(a => a.AlreadyServed);

        var assignmentsByCohort = candidates
            .Where(a => !a.AlreadyServed)
            .GroupBy(a => a.CurrentCohortId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        var slotAssignmentsByCohort = (await LoadSlotAssignmentsAsync(cohortIds, periodNumbers, ct))
            .GroupBy(sa => sa.CohortId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var newPeriods = new List<ServicePeriod>();
        var publishableSlots = new List<SlotAssignmentInfo>();
        int published = 0, skipped = 0;

        foreach (var cohortId in cohortIds)
        {
            if (publishedCohortIds.Contains(cohortId)
                || !slotAssignmentsByCohort.TryGetValue(cohortId, out var slots) || slots.Count == 0
                || !assignmentsByCohort.TryGetValue(cohortId, out var assignmentIds) || assignmentIds.Count == 0)
            {
                skipped++;
                continue;
            }

            publishableSlots.AddRange(slots);
            newPeriods.AddRange(BuildPeriods(slots, assignmentIds));
            published++;
        }

        if (!allowOverCapacity)
        {
            var capacity = await EnsureCapacityAsync(publishableSlots, ct);
            if (capacity.IsFailure)
                return Result.Failure<PublishResult>(capacity.Error);
        }

        if (newPeriods.Count > 0)
        {
            await dbContext.ServicePeriods.AddRangeAsync(newPeriods, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return Result.Success(new PublishResult(published, newPeriods.Count, skipped, skippedAlreadyServed));
    }

    private Task<bool> IsPublishedAsync(int cohortId, CancellationToken ct) =>
        dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == cohortId)
            .AnyAsync(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null), ct);

    private async Task<List<SlotAssignmentInfo>> LoadSlotAssignmentsAsync(
        IReadOnlyCollection<int> cohortIds, IReadOnlyCollection<int>? periodNumbers, CancellationToken ct)
    {
        var query = dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CohortId));

        if (periodNumbers is { Count: > 0 })
            query = query.Where(a => periodNumbers.Contains(a.StageSlot.PeriodNumber));

        return await query
            .Select(a => new SlotAssignmentInfo(
                a.Id, a.CohortId, a.ServiceId, a.StageSlot.StartDate, a.StageSlot.EndDate,
                a.StageSlot.PeriodNumber, a.Service.Name,
                a.Cohort.Stage.LevelId,
                a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                a.Cohort.Stage.RotationMode))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Refuses to publish if any service would be over-booked over an overlapping window — counted
    /// globally across every stage, so a service shared by two partitions running different stages
    /// on overlapping dates cannot be silently over-filled. The cohort being published is already
    /// part of the planned occupancy the lookup measures.
    ///
    /// <b>One</b> ceiling per service, not two — quotas replace the total rather than sitting under
    /// it. A restricted service is measured per promotion against that promotion's quota; an
    /// unrestricted one against its total, across every promotion at once. So a service of 20
    /// granting 10 and 15 publishes 6 + 15 = 21 without complaint, and the same service with no
    /// quotas refuses at 21. See <see cref="Domain.Hospitals.Service.CapacityFor"/>.
    /// </summary>
    private async Task<Result> EnsureCapacityAsync(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, CancellationToken ct)
    {
        if (slotAssignments.Count == 0)
            return Result.Success();

        var serviceIds = slotAssignments.Select(s => s.ServiceId).Distinct().ToList();
        var occupancy = await occupancyCalculator.BuildAsync(serviceIds, ct);
        var intake = await intakeCalculator.BuildAsync(serviceIds, ct);

        foreach (var sa in slotAssignments
                     .GroupBy(s => new { s.ServiceId, s.LevelId, s.StartDate, s.EndDate })
                     .Select(g => g.First()))
        {
            if (intake.HasLevelRestrictions(sa.ServiceId))
            {
                if (!intake.Admits(sa.ServiceId, sa.LevelId))
                    return Result.Failure(StageErrors.LevelNotAdmitted(
                        sa.PeriodNumber, sa.ServiceName, sa.LevelLabel, sa.StartDate, sa.EndDate));

                // This promotion's students only: the quota is about them, and another promotion
                // filling its own quota is not this one's problem.
                int levelLoad = occupancy.LoadOn(sa.ServiceId, sa.LevelId, sa.StartDate, sa.EndDate);
                int levelCapacity = intake.CapacityFor(sa.ServiceId, sa.LevelId);
                if (levelLoad > levelCapacity)
                    return Result.Failure(StageErrors.LevelCapacityExceeded(
                        sa.PeriodNumber, sa.ServiceName, sa.LevelLabel,
                        sa.StartDate, sa.EndDate, levelLoad, levelCapacity));

                continue;
            }

            // Unrestricted: one number for everybody, so the load is everybody. Blaming a "quota"
            // here would send the user looking for a rule nobody authored.
            int load = occupancy.LoadOn(sa.ServiceId, sa.StartDate, sa.EndDate);
            int capacity = intake.TotalCapacity(sa.ServiceId);
            if (load > capacity)
                return Result.Failure(StageErrors.CapacityExceeded(
                    sa.PeriodNumber, sa.ServiceName, sa.StartDate, sa.EndDate, load, capacity));
        }

        return Result.Success();
    }

    /// <summary>
    /// One <see cref="ServicePeriod"/> per student per <i>stay</i>. Under
    /// <see cref="StageRotationMode.PerPeriod"/> a stay is a single cell; under
    /// <see cref="StageRotationMode.SingleService"/> it is the whole run the group spends in one
    /// service, so the run's cells collapse into one continuous period carrying one evaluation.
    /// </summary>
    private static List<ServicePeriod> BuildPeriods(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, IReadOnlyCollection<Guid> assignmentIds)
    {
        var stays = BuildStays(slotAssignments);
        var periods = new List<ServicePeriod>(stays.Count * assignmentIds.Count);

        foreach (var stay in stays)
            foreach (var assignmentId in assignmentIds)
            {
                // Do NOT pre-set the coverage rows' Id: they are children of a brand-new period and
                // EF generates the keys (see InternshipAssignment.Delocalize for the failure mode).
                var period = new ServicePeriod
                {
                    InternshipAssignmentId = assignmentId,
                    ServiceId              = stay.ServiceId,
                    CohortSlotAssignmentId = stay.Cells[0].Id,
                    StartDate              = stay.StartDate,
                    EndDate                = stay.EndDate,
                    IsComplete             = false,
                };

                foreach (var cell in stay.Cells)
                    period.SlotCoverage.Add(new ServicePeriodSlotCoverage
                    {
                        CohortSlotAssignmentId = cell.Id,
                    });

                periods.Add(period);
            }

        return periods;
    }

    /// <summary>
    /// Groups a cohort's cells into the stays they represent.
    ///
    /// <para>A run is a maximal set of that cohort's cells with <b>consecutive period numbers and the
    /// same service</b>. Deriving it from the cells rather than from the caller's window is what makes
    /// it general: publishing one concurrency block and publishing the whole stage both produce the
    /// same stays, because each cohort only ever holds the cells of its own run. Breaking on a service
    /// change matters too — a cell edited by hand to a different service is two stays, not one period
    /// silently spanning both.</para>
    /// </summary>
    private static List<Stay> BuildStays(IReadOnlyCollection<SlotAssignmentInfo> slotAssignments)
    {
        var stays = new List<Stay>();

        foreach (var group in slotAssignments.GroupBy(sa => sa.CohortId))
        {
            var ordered = group.OrderBy(sa => sa.PeriodNumber).ToList();

            if (ordered[0].RotationMode != StageRotationMode.SingleService)
            {
                stays.AddRange(ordered.Select(sa => new Stay([sa], sa.ServiceId, sa.StartDate, sa.EndDate)));
                continue;
            }

            var run = new List<SlotAssignmentInfo> { ordered[0] };
            for (int i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var current  = ordered[i];

                if (current.PeriodNumber == previous.PeriodNumber + 1 && current.ServiceId == previous.ServiceId)
                {
                    run.Add(current);
                    continue;
                }

                stays.Add(Close(run));
                run = [current];
            }
            stays.Add(Close(run));
        }

        return stays;

        static Stay Close(List<SlotAssignmentInfo> run) => new(
            [.. run], run[0].ServiceId, run.Min(c => c.StartDate), run.Max(c => c.EndDate));
    }

    private sealed record Stay(
        IReadOnlyList<SlotAssignmentInfo> Cells, int ServiceId, DateOnly StartDate, DateOnly EndDate);

    private sealed record SlotAssignmentInfo(
        int Id, int CohortId, int ServiceId, DateOnly StartDate, DateOnly EndDate,
        int PeriodNumber, string ServiceName, int LevelId, string LevelLabel,
        StageRotationMode RotationMode);
}
