using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

public sealed record PublishResult(int PublishedCohorts, int PeriodsCreated, int SkippedCohorts);

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

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == cohortId)
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

        var assignmentsByCohort = (await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => new { a.Id, a.CurrentCohortId })
            .ToListAsync(ct))
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

        return Result.Success(new PublishResult(published, newPeriods.Count, skipped));
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
                a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId)))
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

    private static List<ServicePeriod> BuildPeriods(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, IReadOnlyCollection<Guid> assignmentIds)
    {
        var periods = new List<ServicePeriod>(slotAssignments.Count * assignmentIds.Count);
        foreach (var sa in slotAssignments)
            foreach (var assignmentId in assignmentIds)
                periods.Add(new ServicePeriod
                {
                    InternshipAssignmentId = assignmentId,
                    ServiceId              = sa.ServiceId,
                    CohortSlotAssignmentId = sa.Id,
                    StartDate              = sa.StartDate,
                    EndDate                = sa.EndDate,
                    IsComplete             = false,
                });
        return periods;
    }

    private sealed record SlotAssignmentInfo(
        int Id, int CohortId, int ServiceId, DateOnly StartDate, DateOnly EndDate,
        int PeriodNumber, string ServiceName, int LevelId, string LevelLabel);
}
