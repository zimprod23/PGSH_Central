using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// <paramref name="GroupConflicts"/> counts cells that were <b>not</b> written because the group was
/// already placed in an overlapping period of another stage — the normal signal that an arrange was
/// run across every partition where it should have targeted one.
/// </summary>
public sealed record RotationArrangeResult(
    int Assigned, int SaturatedServices, int TotalStudents, int TotalCapacity, int GroupConflicts = 0);

/// <summary>
/// Capacity-proportional cyclic rotation of cohorts across services within one
/// academic year, optionally scoped further to a subset of partitions and/or a
/// window of periods. Scoping is what expresses the macro split: arranging
/// Partition A into periods 1–2 and B into 3–4 of the same stage. Removal of prior
/// cells is restricted to the targeted cohorts × targeted slots, so arranging one
/// partition's window never erases another's. Shared by the auto-arrange command
/// and the macro-plan orchestrator.
///
/// <para>⚠ <b>The service queue is balanced over the cohorts of a single call</b>, so partitions
/// that share a stage over the same window must be passed together in
/// <paramref name="partitionLabels"/>. One call each balances every partition against the full
/// service list in ignorance of the others, and their remainders stack — see
/// <c>MacroPlan.ConcurrencyBlock</c>, which is what groups them.</para>
/// </summary>
internal sealed class RotationArranger(
    IApplicationDbContext dbContext,
    ServiceOccupancyCalculator occupancyCalculator,
    PromotionPartitioning promotionPartitioning,
    Slots.GroupScheduleConflictGuard groupGuard)
{
    public async Task<Result<RotationArrangeResult>> ArrangeAsync(
        int stageId,
        int academicYearId,
        IReadOnlyCollection<string>? partitionLabels,
        IReadOnlyCollection<int>? periodNumbers,
        int? partitionCount,
        CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Include(s => s.AllowedServices)
            .ThenInclude(s => s.LevelCapacities)
            .Include(s => s.Level)
            .FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);

        if (stage is null)
            return Result.Failure<RotationArrangeResult>(StageErrors.NotFound(stageId));

        if (stage.AllowedServices.Count == 0)
            return Result.Failure<RotationArrangeResult>(
                Error.Validation("Schedule.NoAllowedServices",
                    "No allowed services are configured for this stage."));

        // A service that does not take this promotion is not a candidate at all — leaving it in the
        // rotation would place a cohort somewhere publish is then guaranteed to refuse. Its capacity
        // is the level quota, not the physical ceiling: weighting by the ceiling hands a service of
        // 40 that accepts 5 first-years the largest share of the first-year rotation.
        int levelId = stage.LevelId;
        string levelLabel = stage.Level?.Label ?? $"niveau {levelId}";

        var services = stage.AllowedServices
            .Where(s => s.Admits(levelId))
            .OrderBy(s => s.Id)
            .Select(s => new ServiceInfo(s.Id, s.CapacityFor(levelId)))
            .Where(s => s.Capacity > 0)
            .ToList();

        if (services.Count == 0)
            return Result.Failure<RotationArrangeResult>(
                StageErrors.NoServicesAdmitLevel(stage.Name, levelLabel));

        int totalCapacity = services.Sum(s => s.Capacity);

        var allSlots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == stageId && s.AcademicYearId == academicYearId)
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

        // A single-service stage is arranged one run at a time, because the run is what the group
        // spends in one service. The window is therefore not optional: "arrange the whole stage"
        // would otherwise hand a cohort a single service for every column the stage owns — nine
        // months in one service, written silently and looking exactly like a correct plan.
        // ⚠ The macro plan always scopes its calls (a ConcurrencyBlock *is* a run), so this only
        // ever bites the bare auto-arrange path, which is precisely where it should.
        bool singleService = stage.RotationMode == StageRotationMode.SingleService;
        if (singleService)
        {
            if (periodNumbers is not { Count: > 0 } && allSlots.Count > 1)
                return Result.Failure<RotationArrangeResult>(
                    StageErrors.SingleServiceRunNotScoped(stage.Name, allSlots.Count));

            var numbers = slots.Select(s => s.PeriodNumber).Order().ToList();
            if (numbers[^1] - numbers[0] != numbers.Count - 1)
                return Result.Failure<RotationArrangeResult>(
                    StageErrors.SingleServiceRunNotContiguous(stage.Name, numbers));
        }

        var slotIds = slots.Select(s => s.Id).ToList();

        // All cohorts of the stage participate in the queue/rotation so the cycle stays
        // consistent across runs. Cohorts whose cells in the window are already published
        // are not dropped here — only their published cells are protected below.
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId)
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

        // ⚠ The cut is read from the PROMOTION, not from this stage's cohorts. PartitionAllocator
        // takes "the existing partition count" from the labels it is shown, and a stage routinely
        // reaches only part of its promotion — so passing the stage's own cohorts showed a promotion
        // cut into ten as one cut into two, and the gap-fill wrote those two labels onto real rosters.
        // See PromotionPartitioning for the measurement.
        var cut = await promotionPartitioning.ReadAsync(academicYearId, levelId, cancellationToken);
        bool alreadyCut = cut.IsCut;

        // Fill the gaps in an existing cut, and cut a fresh promotion only when the caller stated
        // into how many.
        //
        // ⚠ The count is never inferred from the stage's service list. That was the fallback here,
        // and a stage's service count is not a statement about how a promotion should be divided:
        // Santé Publique has one service, so arranging it first cut the whole promotion one-way and
        // every later stage inherited that. Cutting a promotion is a deliberate act with its own
        // command — a strategy, a published-cells refusal and an audit entry
        // (AssignRotationGroupsCommand). Inventing one here bypassed all three.
        var promotionFill = alreadyCut || partitionCount is not null
            ? cut.FillGaps(partitionCount ?? 1)
            : [];

        // Balanced over the promotion, but written only for the rosters this arrange is actually
        // placing. An arrange has no mandate to partition a roster it never touches — that is
        // AssignRotationGroupsCommand's act, with its own guards and its own audit entry. The rest of
        // the promotion keeps its gaps, and the next fill is balanced against the real state again.
        var placeable = cohorts.Select(c => c.AcademicGroupId).ToHashSet();
        var newlyAssigned = promotionFill
            .Where(kv => placeable.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (newlyAssigned.Count > 0)
        {
            var groupsToUpdate = await dbContext.AcademicGroups
                .Where(g => newlyAssigned.Keys.Contains(g.Id))
                .ToListAsync(cancellationToken);

            foreach (var group in groupsToUpdate)
                group.RotationGroup = newlyAssigned[group.Id];
        }

        // Genuinely nullable: a promotion nobody has cut carries no label at all, and pretending
        // otherwise is what let the missing cut pass unnoticed.
        string? LabelOf(CohortInfo c) => c.RotationGroup ?? newlyAssigned.GetValueOrDefault(c.AcademicGroupId);

        var targetCohorts = cohorts;
        if (partitionLabels is { Count: > 0 })
        {
            var wanted = partitionLabels.ToHashSet();
            targetCohorts = cohorts.Where(c => LabelOf(c) is { } label && wanted.Contains(label)).ToList();
        }

        if (targetCohorts.Count == 0)
        {
            // A partition was asked for and nothing carries one — the promotion has never been cut.
            // Reported rather than returned as "0 cells", which is indistinguishable from a run that
            // had nothing left to do. A label that simply has no cohort *here* is a different case
            // and stays silent: CohortProvisioner legitimately skips a stage the group's CNPN does
            // not require, and the macro plan counts that separately.
            if (partitionLabels is { Count: > 0 } && !alreadyCut && newlyAssigned.Count == 0)
                return Result.Failure<RotationArrangeResult>(
                    StageErrors.PromotionNotPartitioned(stage.Name, levelLabel));

            return Result.Success(new RotationArrangeResult(0, 0, cohorts.Sum(c => c.StudentCount), totalCapacity));
        }

        // Partition order first (A→B→C…), then group number — keeps each partition's
        // cohorts contiguous so the cyclic shift moves whole partition blocks together.
        var ordered = targetCohorts
            .OrderBy(LabelOf)
            .ThenBy(c => c.GroupNumber)
            .ToList();

        var targetCohortIds = ordered.Select(c => c.Id).ToList();

        // Where these groups already sit OUTSIDE the window being rewritten. Two stages of a level
        // may share a window — that is how partitions are planned — so the conflict that matters is
        // per group, not per date column: arranging Chirurgie P1 for a partition already placed in
        // Médecine P1 would put those students in two services at once.
        //
        // ⚠ This is computed *before* the removal below, and the conflicting pairs are excluded from
        // it. Deciding it afterwards deleted the cohort's existing cell and then declined to write a
        // replacement, so re-running an arrange across all partitions silently destroyed a good plan.
        var occupiedElsewhere = await groupGuard.BuildAsync(
            ordered.Select(c => c.AcademicGroupId).Distinct().ToList(),
            slotIds,
            cancellationToken);

        var conflicting = new HashSet<(int CohortId, int SlotId)>();
        foreach (var slot in slots)
        {
            foreach (var cohort in ordered)
            {
                if (occupiedElsewhere.ConflictFor(cohort.AcademicGroupId, slot.StartDate, slot.EndDate) is not null)
                    conflicting.Add((cohort.Id, slot.Id));
            }
        }

        // Scoped removal: only the targeted cohorts within the targeted slots — but a cell
        // that is already published (a ServicePeriod points at it) is a locked execution
        // record. It is never deleted nor rewritten, so a started stage keeps its history
        // while its newly-added periods can still be arranged.
        var existingCells = await dbContext.CohortSlotAssignments
            .Where(a => targetCohortIds.Contains(a.CohortId) && slotIds.Contains(a.StageSlotId))
            .Select(a => new { a.Id, a.CohortId, a.StageSlotId })
            .ToListAsync(cancellationToken);

        // ⚠ Via the coverage table, never via ServicePeriod.CohortSlotAssignmentId: that FK names only
        // the first cell of a run, so under SingleService the trailing cells of a published run would
        // read as free and be rewritten underneath a stage already underway. See PublishedCells.
        var existingCellIds = existingCells.Select(e => e.Id).ToList();
        var lockedCellIdSet = await dbContext.PublishedAmongAsync(existingCellIds, cancellationToken);
        var lockedCells = existingCells
            .Where(e => lockedCellIdSet.Contains(e.Id))
            .Select(e => (e.CohortId, e.StageSlotId))
            .ToHashSet();

        // A cell that will be refused is left exactly as it is: it is not stale, it is simply out of
        // this run's reach.
        var staleIds = existingCells
            .Where(e => !lockedCellIdSet.Contains(e.Id))
            .Where(e => !conflicting.Contains((e.CohortId, e.StageSlotId)))
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

        // Conflicting cells are skipped and counted rather than failing the whole run, so a
        // partially targeted arrange still does the part it legitimately can.
        var newAssignments = new List<CohortSlotAssignment>(n * slots.Count);
        int groupConflicts = 0;

        // The whole run shares the phase of its first column when the stage keeps the group in one
        // service: that is the entire mechanical difference between the two modes. Advancing per
        // column is what moves a cohort S1 → S2 → S3; freezing the offset leaves it where it is,
        // and the publisher then collapses the run's cells into one period with one evaluation.
        // The phase is still taken from the run's start rather than fixed, so two partitions doing
        // the stage in different windows still land on different services.
        int runOffset = phaseBySlotId[slots.MinBy(s => s.PeriodNumber)!.Id] * shiftPerSlot;

        foreach (var slot in slots)
        {
            int offset = singleService ? runOffset : phaseBySlotId[slot.Id] * shiftPerSlot;
            for (int ci = 0; ci < n; ci++)
            {
                if (lockedCells.Contains((targetCohortIds[ci], slot.Id)))
                    continue;

                if (conflicting.Contains((targetCohortIds[ci], slot.Id)))
                {
                    groupConflicts++;
                    continue;
                }

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
        // One ceiling each, matching what publish will enforce: a restricted service is measured on
        // this promotion alone against its quota, an unrestricted one on everybody against its total.
        var occupancy = await occupancyCalculator.BuildAsync(
            services.Select(s => s.Id).ToList(), cancellationToken);

        var restricted = stage.AllowedServices
            .Where(s => s.HasLevelRestrictions)
            .Select(s => s.Id)
            .ToHashSet();

        // services[] already holds CapacityFor(levelId) — the quota for a restricted service, the
        // total for an open one.
        var capacityByService = services.ToDictionary(s => s.Id, s => s.Capacity);

        int saturatedServices = slots
            .SelectMany(slot => newAssignments
                .Where(a => a.StageSlotId == slot.Id)
                .Select(a => a.ServiceId)
                .Where(serviceId =>
                    (restricted.Contains(serviceId)
                        ? occupancy.LoadOn(serviceId, levelId, slot.StartDate, slot.EndDate)
                        : occupancy.LoadOn(serviceId, slot.StartDate, slot.EndDate))
                    > capacityByService.GetValueOrDefault(serviceId)))
            .Distinct()
            .Count();

        return Result.Success(new RotationArrangeResult(
            newAssignments.Count,
            saturatedServices,
            ordered.Sum(c => c.StudentCount),
            totalCapacity,
            groupConflicts));
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
