using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule.AutoArrange;

internal sealed class AutoArrangeStageScheduleCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AutoArrangeStageScheduleCommand, AutoArrangeResult>
{
    public async Task<Result<AutoArrangeResult>> Handle(
        AutoArrangeStageScheduleCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Include(s => s.AllowedServices)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure<AutoArrangeResult>(StageErrors.NotFound(request.StageId));

        var services = stage.AllowedServices
            .OrderBy(s => s.Id)
            .Select(s => new ServiceInfo(s.Id, s.Capacity))
            .ToList();

        if (services.Count == 0)
            return Result.Failure<AutoArrangeResult>(
                Error.Validation("Schedule.NoAllowedServices",
                    "No allowed services are configured for this stage."));

        var slots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == request.StageId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (slots.Count == 0)
            return Result.Failure<AutoArrangeResult>(
                Error.Validation("Schedule.NoSlots",
                    "No time slots are defined for this stage."));

        // Load cohorts with their academic group partition info and current student count
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId
                     && !c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)))
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .Select(c => new CohortInfo(
                c.Id,
                c.AcademicGroupId,
                c.AcademicGroup.GroupNumber,
                c.AcademicGroup.RotationGroup,
                c.Assignments.Count))
            .ToListAsync(cancellationToken);

        if (cohorts.Count == 0)
            return Result.Success(new AutoArrangeResult(0, 0, 0, services.Sum(s => s.Capacity)));

        // Remove existing unpublished slot assignments before rewriting
        var cohortIds = cohorts.Select(c => c.Id).ToList();
        var existing = await dbContext.CohortSlotAssignments
            .Where(a => cohortIds.Contains(a.CohortId))
            .ToListAsync(cancellationToken);
        dbContext.CohortSlotAssignments.RemoveRange(existing);

        // ── Partition assignment ───────────────────────────────────────────────
        //
        // RotationGroup is a persistent label on AcademicGroup (A, B, C…) that
        // identifies which rotation track a group belongs to, across ALL stages of
        // the academic year. Groups that already carry a label keep it; new groups
        // are distributed evenly across partitions in GroupNumber order.
        //
        // numPartitions:
        //   - If any groups already have labels → use the existing partition count
        //     (preserves the structure set up on a previous stage's auto-arrange).
        //   - Otherwise → use PartitionCount from the request, or default to the
        //     number of allowed services (natural: one partition per starting block).

        var existingLabels = cohorts
            .Select(c => c.RotationGroup)
            .OfType<string>()
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        int numPartitions = existingLabels.Count > 0
            ? existingLabels.Count
            : Math.Max(1, request.PartitionCount ?? services.Count);

        var labels = Enumerable.Range(0, numPartitions)
            .Select(i => ((char)('A' + (i % 26))).ToString() + (i >= 26 ? (i / 26).ToString() : ""))
            .ToList();

        // Ensure any labels already in use are represented (edge case: manually set value outside range)
        foreach (var l in existingLabels.Where(l => !labels.Contains(l)))
            labels.Add(l);

        // Assign unassigned cohorts round-robin into the smallest partition
        var partitionCounts = labels.ToDictionary(l => l, l => cohorts.Count(c => c.RotationGroup == l));
        var unassigned      = cohorts.Where(c => c.RotationGroup is null).OrderBy(c => c.GroupNumber).ToList();
        var newlyAssigned   = new Dictionary<int, string>(); // AcademicGroupId → label

        foreach (var cohort in unassigned)
        {
            var label = partitionCounts.MinBy(kvp => kvp.Value).Key;
            newlyAssigned[cohort.AcademicGroupId] = label;
            partitionCounts[label]++;
        }

        // Persist new rotation group labels on the academic groups
        if (newlyAssigned.Count > 0)
        {
            var groupsToUpdate = await dbContext.AcademicGroups
                .Where(g => newlyAssigned.Keys.Contains(g.Id))
                .ToListAsync(cancellationToken);

            foreach (var group in groupsToUpdate)
                group.RotationGroup = newlyAssigned[group.Id];
        }

        // Final sorted cohort list: partition order A→B→C…, then group number within each partition
        var sortedCohortIds = cohorts
            .Select(c => c with { RotationGroup = c.RotationGroup ?? newlyAssigned.GetValueOrDefault(c.AcademicGroupId) })
            .OrderBy(c => c.RotationGroup)
            .ThenBy(c => c.GroupNumber)
            .Select(c => c.Id)
            .ToList();

        int n = sortedCohortIds.Count;

        // ── Capacity-proportional cyclic rotation ─────────────────────────────
        //
        // Each service is allocated a number of cohort-slots proportional to how
        // many full cohorts it can host per period (student-aware largest-remainder).
        //
        // When students are assigned:   weight_i = max(1, floor(capacity_i / avgStudents))
        //   → services too small for 1 cohort still get weight 1 (will saturate,
        //     but including them keeps all allowed services in the rotation).
        //   → services large enough for k cohorts get weight k, so their share of
        //     the queue scales linearly with actual student-throughput.
        //
        // When no students are assigned (planning phase): fall back to raw capacity
        // proportionality so the rotation can be previewed before assigning students.
        //
        // The resulting serviceQueue of length N is read with a cyclic offset of
        // slot × (N / numSlots) so every cohort visits a different service section
        // each period. Cohorts sorted by partition first keeps all partitions
        // contiguous, matching the faculty rotation documents.

        int totalStudents = cohorts.Sum(c => c.StudentCount);
        double avgStudents = totalStudents > 0 ? (double)totalStudents / n : 0;

        List<double> portions;
        if (avgStudents > 0)
        {
            var weights = services
                .Select(s => (double)Math.Max(1, (int)(s.Capacity / avgStudents)))
                .ToList();
            double totalWeight = weights.Sum();
            portions = weights.Select(w => w / totalWeight * n).ToList();
        }
        else
        {
            double totalCap = services.Sum(s => (double)s.Capacity);
            portions = services.Select(s => s.Capacity / totalCap * n).ToList();
        }

        var allocated   = portions.Select(p => (int)p).ToList();
        int leftover    = n - allocated.Sum();

        services
            .Select((_, i) => (i, frac: portions[i] - allocated[i]))
            .OrderByDescending(x => x.frac)
            .Take(leftover)
            .ToList()
            .ForEach(x => allocated[x.i]++);

        var serviceQueue = services
            .SelectMany((s, i) => Enumerable.Repeat(s.Id, Math.Max(0, allocated[i])))
            .ToList();

        while (serviceQueue.Count < n) serviceQueue.Add(services.MaxBy(s => s.Capacity)!.Id);
        if (serviceQueue.Count > n)    serviceQueue = serviceQueue.Take(n).ToList();

        int shiftPerSlot = slots.Count > 1 ? n / slots.Count : 0;

        var newAssignments = new List<CohortSlotAssignment>(n * slots.Count);
        foreach (var (slotId, slotIdx) in slots.Select((s, i) => (s, i)))
        {
            int offset = slotIdx * shiftPerSlot;
            for (int ci = 0; ci < n; ci++)
            {
                newAssignments.Add(new CohortSlotAssignment
                {
                    CohortId    = sortedCohortIds[ci],
                    StageSlotId = slotId,
                    ServiceId   = serviceQueue[(ci + offset) % serviceQueue.Count],
                });
            }
        }

        await dbContext.CohortSlotAssignments.AddRangeAsync(newAssignments, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // A service is saturated when its assigned student load exceeds its capacity.
        // We report the count so the frontend can guide the admin.
        int saturatedServices = avgStudents > 0
            ? services.Where((s, i) => allocated[i] * avgStudents > s.Capacity).Count()
            : 0;

        return Result.Success(new AutoArrangeResult(
            newAssignments.Count,
            saturatedServices,
            totalStudents,
            services.Sum(s => s.Capacity)));
    }

    private sealed record ServiceInfo(int Id, int Capacity);
    private sealed record CohortInfo(int Id, int AcademicGroupId, int GroupNumber, string? RotationGroup, int StudentCount);
}
