using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

public sealed record RotationArrangeResult(int Assigned, int SaturatedServices, int TotalStudents, int TotalCapacity);

/// <summary>
/// Capacity-proportional cyclic rotation of cohorts across services, optionally
/// scoped to a subset of partitions and/or a window of periods. Scoping is what
/// expresses the macro split: arranging Partition A into periods 1–2 and B into
/// 3–4 of the same stage. Removal of prior cells is restricted to the targeted
/// cohorts × targeted slots, so arranging one partition's window never erases
/// another's. Shared by the auto-arrange command and the macro-plan orchestrator.
/// </summary>
internal sealed class RotationArranger(IApplicationDbContext dbContext, ServiceOccupancyCalculator occupancyCalculator)
{
    public async Task<Result<RotationArrangeResult>> ArrangeAsync(
        int stageId,
        IReadOnlyCollection<string>? partitionLabels,
        IReadOnlyCollection<int>? periodNumbers,
        int? partitionCount,
        CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Include(s => s.AllowedServices)
            .FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);

        if (stage is null)
            return Result.Failure<RotationArrangeResult>(StageErrors.NotFound(stageId));

        var services = stage.AllowedServices
            .OrderBy(s => s.Id)
            .Select(s => new ServiceInfo(s.Id, s.Capacity))
            .ToList();

        if (services.Count == 0)
            return Result.Failure<RotationArrangeResult>(
                Error.Validation("Schedule.NoAllowedServices",
                    "No allowed services are configured for this stage."));

        int totalCapacity = services.Sum(s => s.Capacity);

        var allSlots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == stageId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new { s.Id, s.PeriodNumber, s.StartDate, s.EndDate })
            .ToListAsync(cancellationToken);

        if (allSlots.Count == 0)
            return Result.Failure<RotationArrangeResult>(
                Error.Validation("Schedule.NoSlots",
                    "No time slots are defined for this stage."));

        var slots = periodNumbers is { Count: > 0 }
            ? allSlots.Where(s => periodNumbers.Contains(s.PeriodNumber)).ToList()
            : allSlots;

        if (slots.Count == 0)
            return Result.Failure<RotationArrangeResult>(
                Error.Validation("Schedule.NoSlots",
                    "No time slots are defined for this stage in the selected window."));

        var slotIds = slots.Select(s => s.Id).ToList();

        // All cohorts of the stage participate in the queue/rotation so the cycle stays
        // consistent across runs. Cohorts whose cells in the window are already published
        // are not dropped here — only their published cells are protected below.
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId)
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .Select(c => new CohortInfo(
                c.Id,
                c.AcademicGroupId,
                c.AcademicGroup.GroupNumber,
                c.AcademicGroup.RotationGroup,
                c.Assignments.Count))
            .ToListAsync(cancellationToken);

        if (cohorts.Count == 0)
            return Result.Success(new RotationArrangeResult(0, 0, 0, totalCapacity));

        // Ensure every group involved carries a partition label (persist new ones),
        // so partition filtering is meaningful even on a stage arranged for the first time.
        var newlyAssigned = PartitionAllocator.AssignUnlabelled(
            cohorts.Select(c => (c.AcademicGroupId, c.RotationGroup)).ToList(),
            partitionCount ?? services.Count);

        if (newlyAssigned.Count > 0)
        {
            var groupsToUpdate = await dbContext.AcademicGroups
                .Where(g => newlyAssigned.Keys.Contains(g.Id))
                .ToListAsync(cancellationToken);

            foreach (var group in groupsToUpdate)
                group.RotationGroup = newlyAssigned[group.Id];
        }

        string LabelOf(CohortInfo c) => c.RotationGroup ?? newlyAssigned.GetValueOrDefault(c.AcademicGroupId)!;

        var targetCohorts = cohorts;
        if (partitionLabels is { Count: > 0 })
        {
            var wanted = partitionLabels.ToHashSet();
            targetCohorts = cohorts.Where(c => wanted.Contains(LabelOf(c))).ToList();
        }

        if (targetCohorts.Count == 0)
            return Result.Success(new RotationArrangeResult(0, 0, cohorts.Sum(c => c.StudentCount), totalCapacity));

        // Partition order first (A→B→C…), then group number — keeps each partition's
        // cohorts contiguous so the cyclic shift moves whole partition blocks together.
        var ordered = targetCohorts
            .OrderBy(LabelOf)
            .ThenBy(c => c.GroupNumber)
            .ToList();

        var targetCohortIds = ordered.Select(c => c.Id).ToList();

        // Scoped removal: only the targeted cohorts within the targeted slots — but a cell
        // that is already published (a ServicePeriod points at it) is a locked execution
        // record. It is never deleted nor rewritten, so a started stage keeps its history
        // while its newly-added periods can still be arranged.
        var existingCells = await dbContext.CohortSlotAssignments
            .Where(a => targetCohortIds.Contains(a.CohortId) && slotIds.Contains(a.StageSlotId))
            .Select(a => new { a.Id, a.CohortId, a.StageSlotId })
            .ToListAsync(cancellationToken);

        var existingCellIds = existingCells.Select(e => e.Id).ToList();
        var lockedCellIds = existingCellIds.Count == 0
            ? new List<int>()
            : await dbContext.ServicePeriods
                .Where(p => p.CohortSlotAssignmentId != null
                         && existingCellIds.Contains(p.CohortSlotAssignmentId.Value))
                .Select(p => p.CohortSlotAssignmentId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

        var lockedCellIdSet = lockedCellIds.ToHashSet();
        var lockedCells = existingCells
            .Where(e => lockedCellIdSet.Contains(e.Id))
            .Select(e => (e.CohortId, e.StageSlotId))
            .ToHashSet();

        var staleIds = existingCells
            .Where(e => !lockedCellIdSet.Contains(e.Id))
            .Select(e => e.Id)
            .ToList();

        if (staleIds.Count > 0)
        {
            var stale = await dbContext.CohortSlotAssignments
                .Where(a => staleIds.Contains(a.Id))
                .ToListAsync(cancellationToken);
            dbContext.CohortSlotAssignments.RemoveRange(stale);
        }

        int n = ordered.Count;
        var serviceQueue = BuildServiceQueue(services, ordered, n);

        // The rotation cycle is anchored to the cohort set's actual participation footprint:
        // the slots they ALREADY occupy in this stage, plus the slots being arranged now.
        // Each slot's phase = its position in that ordered footprint, and the step spans the
        // footprint length. This gives two correct behaviours from one rule:
        //   • macro matrix (a partition runs a single window, e.g. A→P1-2): footprint = the
        //     window, so the clean half-cycle swap is preserved — no regression.
        //   • adding new periods to an already-arranged set: footprint grows to include them,
        //     so the new periods get fresh phases and CONTINUE the rotation instead of
        //     repeating the services the cohorts already did.
        var priorSlotIds = await dbContext.CohortSlotAssignments
            .Where(a => targetCohortIds.Contains(a.CohortId) && a.Cohort.StageId == stageId)
            .Select(a => a.StageSlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var footprintSet = priorSlotIds.Concat(slotIds).ToHashSet();
        var phaseBySlotId = allSlots
            .Where(s => footprintSet.Contains(s.Id))
            .Select((s, i) => (s.Id, Phase: i))
            .ToDictionary(x => x.Id, x => x.Phase);

        int cycleLength = phaseBySlotId.Count;
        int shiftPerSlot = cycleLength > 1 ? n / cycleLength : 0;

        var newAssignments = new List<CohortSlotAssignment>(n * slots.Count);
        foreach (var slot in slots)
        {
            int offset = phaseBySlotId[slot.Id] * shiftPerSlot;
            for (int ci = 0; ci < n; ci++)
            {
                if (lockedCells.Contains((targetCohortIds[ci], slot.Id)))
                    continue;

                newAssignments.Add(new CohortSlotAssignment
                {
                    CohortId    = targetCohortIds[ci],
                    StageSlotId = slot.Id,
                    ServiceId   = serviceQueue[(ci + offset) % serviceQueue.Count],
                });
            }
        }

        await dbContext.CohortSlotAssignments.AddRangeAsync(newAssignments, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Saturation is measured after the save against the global load on each service —
        // a service over-filled by another stage/partition over an overlapping window counts here too.
        var occupancy = await occupancyCalculator.BuildAsync(
            services.Select(s => s.Id).ToList(), cancellationToken);
        var capacityByService = services.ToDictionary(s => s.Id, s => s.Capacity);

        int saturatedServices = slots
            .SelectMany(slot => newAssignments
                .Where(a => a.StageSlotId == slot.Id)
                .Select(a => a.ServiceId)
                .Where(serviceId => occupancy.LoadOn(serviceId, slot.StartDate, slot.EndDate)
                                    > capacityByService.GetValueOrDefault(serviceId)))
            .Distinct()
            .Count();

        return Result.Success(new RotationArrangeResult(
            newAssignments.Count,
            saturatedServices,
            ordered.Sum(c => c.StudentCount),
            totalCapacity));
    }

    private static List<int> BuildServiceQueue(List<ServiceInfo> services, List<CohortInfo> cohorts, int n)
    {
        int totalStudents = cohorts.Sum(c => c.StudentCount);
        double avgStudents = totalStudents > 0 ? (double)totalStudents / n : 0;

        List<double> portions;
        if (avgStudents > 0)
        {
            // Weight = how many whole average-sized cohorts a service can actually hold.
            // A service smaller than one cohort gets weight 0 and is left out of the
            // rotation — forcing a full group into it would always overflow (groups are
            // atomic). When total cohort-capacity ≥ N this guarantees no service is
            // over-filled; only a genuine shortfall (capacity < demand) saturates.
            var weights = services
                .Select(s => (double)(int)(s.Capacity / avgStudents))
                .ToList();
            double totalWeight = weights.Sum();

            portions = totalWeight > 0
                ? weights.Select(w => w / totalWeight * n).ToList()
                : CapacityProportions(services, n); // every service smaller than a cohort — degenerate
        }
        else
        {
            portions = CapacityProportions(services, n);
        }

        var allocated = portions.Select(p => (int)p).ToList();
        int leftover  = n - allocated.Sum();

        services
            .Select((_, i) => (i, frac: portions[i] - allocated[i]))
            .OrderByDescending(x => x.frac)
            .Take(leftover)
            .ToList()
            .ForEach(x => allocated[x.i]++);

        var queue = services
            .SelectMany((s, i) => Enumerable.Repeat(s.Id, Math.Max(0, allocated[i])))
            .ToList();

        while (queue.Count < n) queue.Add(services.MaxBy(s => s.Capacity)!.Id);
        if (queue.Count > n)    queue = queue.Take(n).ToList();

        return queue;
    }

    // Raw-capacity proportions — used for the planning preview (no students yet) and as a
    // fallback when every allowed service is smaller than a single cohort.
    private static List<double> CapacityProportions(List<ServiceInfo> services, int n)
    {
        double totalCap = services.Sum(s => (double)s.Capacity);
        return totalCap > 0
            ? services.Select(s => s.Capacity / totalCap * n).ToList()
            : services.Select(_ => (double)n / services.Count).ToList();
    }

    private sealed record ServiceInfo(int Id, int Capacity);
    private sealed record CohortInfo(int Id, int AcademicGroupId, int GroupNumber, string? RotationGroup, int StudentCount);
}
