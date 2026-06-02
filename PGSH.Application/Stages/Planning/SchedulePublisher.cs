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
internal sealed class SchedulePublisher(IApplicationDbContext dbContext)
{
    public async Task<Result> PublishCohortAsync(int cohortId, CancellationToken ct)
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

        var periods = BuildPeriods(slotAssignments, assignmentIds);
        await dbContext.ServicePeriods.AddRangeAsync(periods, ct);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PublishResult>> PublishStageAsync(
        int stageId,
        IReadOnlyCollection<string>? partitionLabels,
        IReadOnlyCollection<int>? periodNumbers,
        CancellationToken ct)
    {
        var cohortQuery = dbContext.Cohorts.AsNoTracking().Where(c => c.StageId == stageId);

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

            newPeriods.AddRange(BuildPeriods(slots, assignmentIds));
            published++;
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
                a.Id, a.CohortId, a.ServiceId, a.StageSlot.StartDate, a.StageSlot.EndDate))
            .ToListAsync(ct);
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

    private sealed record SlotAssignmentInfo(int Id, int CohortId, int ServiceId, DateOnly StartDate, DateOnly EndDate);
}
