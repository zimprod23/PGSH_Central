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
/// <para>⚠ <b>The service queue is balanced over one column at a time</b> — the cohorts this call
/// actually writes into a single period — because that, and not the call's whole reach, is who
/// stands in the stage at the same time. Partitions that share a stage over the same window must
/// still be passed together in <paramref name="partitionLabels"/>: one call each balances every
/// partition against the full service list in ignorance of the others, and their remainders stack.
/// See <c>MacroPlan.ConcurrencyBlock</c>, which is what groups them.</para>
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
        var cohorts = await CohortsQuery(dbContext, stageId, academicYearId).ToListAsync(cancellationToken);

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

        int n = ordered.Count;

        // ⚠ The set that has to be balanced is the cohorts standing in the stage AT THE SAME TIME —
        // one column of the axis — never every cohort the call happens to reach. The two coincide
        // when the caller scopes to a concurrency block, and they do not on "arrange this whole
        // stage": the crossover leaves exactly one partition per column, every other cell being
        // refused as a group conflict, while the queue was built over all P partitions and indexed
        // by each cohort's position in the whole ordered list. Partitions are contiguous in that
        // ordering and each service owns a contiguous run of the queue, so an entire partition fell
        // inside one service's run.
        //
        // Measured on 5MED Psychiatrie (60 groups, 9 partitions, 5 services, 2026-08-18): all nine
        // columns went to a single service — 69 to 85 students against a capacity of 20 — and two of
        // the five services were never used all year. Nothing in the result said so: 60 cells
        // written, and the conflicts reported were the ones the crossover is made of.
        //
        // Computed before anything is removed, because the guard below refuses on it.
        var columnBySlotId = slots.ToDictionary(
            slot => slot.Id,
            slot => Enumerable.Range(0, n)
                .Where(ci => !lockedCells.Contains((targetCohortIds[ci], slot.Id))
                          && !conflicting.Contains((targetCohortIds[ci], slot.Id)))
                .ToList());

        // ⚠ Nobody has crossed over into this stage yet, so it would take the whole promotion for the
        // whole axis. Unscoped, the arrange has nothing to refuse — no other stage claims these
        // groups over these windows — so it writes a cell for every (cohort × column): every roster
        // doing this one stage all year, silently, looking exactly like a plan. Every stage arranged
        // afterwards then gets nothing, because everyone is busy everywhere. Med6 is in precisely
        // this state today (six stages, ten columns, zero cells), so whichever button is pressed
        // first would decide the year.
        //
        // Which partition takes which columns is the crossover, and the crossover is *authored*
        // — the rotation block, or the macro matrix — never inferred from an empty grid.
        // SingleService refuses the same call for the same reason (SingleServiceRunNotScoped); this
        // is PerPeriod's half of that guard, which it never had because the damage is less visible.
        // ⚠ Two conditions narrow it, and both are load-bearing:
        //   • It fires only on the call that names *nothing* — no partition and no window. Naming
        //     either is authored targeting: « A → Médecine P1-P2, B → Chirurgie P1-P2 » is the
        //     faculty's own layout, and there a partition legitimately takes every column of a stage.
        //   • …and only when another stage of the promotion declares the same windows. A stage that
        //     *is* the whole axis starves nobody: there is no crossover to author, and refusing it
        //     would block the ordinary one-stage block.
        if (!singleService
            && partitionLabels is not { Count: > 0 }
            && periodNumbers is not { Count: > 0 }
            && allSlots.Count > 1)
        {
            int widest = columnBySlotId.Values
                .SelectMany(column => column)
                .GroupBy(ci => ci)
                .Select(g => g.Count())
                .DefaultIfEmpty(0)
                .Max();

            if (widest == allSlots.Count)
            {
                var spanStart = slots.Min(s => s.StartDate);
                var spanEnd   = slots.Max(s => s.EndDate);

                bool sharesTheAxis = await dbContext.StageSlots
                    .AsNoTracking()
                    .AnyAsync(
                        s => s.StageId != stageId
                          && s.Stage.LevelId == levelId
                          && s.AcademicYearId == academicYearId
                          && s.StartDate <= spanEnd && spanStart <= s.EndDate,
                        cancellationToken);

                if (sharesTheAxis)
                    return Result.Failure<RotationArrangeResult>(
                        StageErrors.StageWouldFillEveryColumn(stage.Name, allSlots.Count));
            }
        }

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

        // Conflicting cells are skipped and counted rather than failing the whole run, so a
        // partially targeted arrange still does the part it legitimately can. A cell that is both
        // locked and conflicting counts as locked — it is not this run's to place either way.
        int groupConflicts = slots.Sum(slot => Enumerable.Range(0, n).Count(ci =>
            !lockedCells.Contains((targetCohortIds[ci], slot.Id))
            && conflicting.Contains((targetCohortIds[ci], slot.Id))));

        // Capacity-proportional over the column, then rotated by the column's phase so a group doing
        // several columns of the stage moves between services rather than repeating one.
        // ⚠ The step is at least 1: a column smaller than the cycle gave a step of 0, i.e. the same
        // service for every column of a PerPeriod run — SingleService by accident.
        Dictionary<int, int> Place(List<int> column, int phase)
        {
            int m = column.Count;
            if (m == 0) return [];

            var queue  = BuildServiceQueue(services, column.Select(ci => ordered[ci]).ToList(), m, phase);
            int step   = cycleLength > 1 ? Math.Max(1, m / cycleLength) : 0;
            int offset = phase * step;

            return column
                .Select((ci, i) => (ci, ServiceId: queue[(i + offset) % queue.Count]))
                .ToDictionary(x => x.ci, x => x.ServiceId);
        }

        // The whole run shares one placement when the stage keeps the group in one service: that is
        // the entire mechanical difference between the two modes. Advancing per column is what moves
        // a cohort S1 → S2 → S3; deciding once leaves it where it is, and the publisher then
        // collapses the run's cells into one period with one evaluation. It is decided over everyone
        // the run touches and from the run's first phase rather than a fixed one, so two partitions
        // doing the stage in different windows still land on different services.
        var runPlacement = singleService
            ? Place(columnBySlotId.Values.SelectMany(c => c).Distinct().Order().ToList(),
                    phaseBySlotId[slots.MinBy(s => s.PeriodNumber)!.Id])
            : null;

        var newAssignments = new List<CohortSlotAssignment>(n * slots.Count);

        foreach (var slot in slots)
        {
            var column    = columnBySlotId[slot.Id];
            var placement = runPlacement ?? Place(column, phaseBySlotId[slot.Id]);

            foreach (int ci in column)
            {
                newAssignments.Add(new CohortSlotAssignment
                {
                    CohortId    = targetCohortIds[ci],
                    StageSlotId = slot.Id,
                    ServiceId   = placement[ci],
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

    /// <param name="rotate">
    /// Which service the remainder starts from. ⚠ It matters because a column's shape is fixed —
    /// seven groups over five services is 2,2,1,1,1 whatever happens, since the column indexes every
    /// queue position exactly once — so the only thing left to decide is <i>which</i> services carry
    /// the pair. The fractions are equal whenever the capacities are, and they usually are (all 148
    /// imported services carry the same default), so a stable tie-break handed every column of the
    /// year to the same two leading services: over capacity in every période while three sat at
    /// half. Rotating by the column's phase spreads that across the axis.
    /// </param>
    private static List<int> BuildServiceQueue(
        List<ServiceInfo> services, List<CohortInfo> cohorts, int n, int rotate = 0)
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

        int count = services.Count;
        services
            .Select((_, i) => (i, frac: portions[i] - allocated[i]))
            .OrderByDescending(x => x.frac)
            .ThenBy(x => ((x.i - rotate) % count + count) % count)
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

    /// <summary>
    /// The stage's cohorts for one year, each carrying how many students stand in it — which is what
    /// the service queue is weighted by.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>c.Assignments.Count</c> is an aggregate over a navigation collection inside the
    /// projection. It translates to a correlated <c>COUNT</c>, but the query that took down the macro
    /// plan on 2026-08-26 was a navigation collection in a projection too — the line between the two
    /// is the provider's, not one that can be reasoned about from the C#. Named so
    /// <c>SqlTranslationTests</c> can compile it.
    /// </remarks>
    internal static IQueryable<CohortInfo> CohortsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId)
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .Select(c => new CohortInfo(
                c.Id,
                c.AcademicGroupId,
                c.AcademicGroup.GroupNumber,
                c.AcademicGroup.RotationGroup,
                c.Assignments.Count));

    private sealed record ServiceInfo(int Id, int Capacity);

    internal sealed record CohortInfo(int Id, int AcademicGroupId, int GroupNumber, string? RotationGroup, int StudentCount);
}
