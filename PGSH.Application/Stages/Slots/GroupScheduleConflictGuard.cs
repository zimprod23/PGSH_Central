using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

/// <summary>Where a group already is: one period it occupies, with the window and the stage.</summary>
public sealed record GroupPlacement(
    int AcademicGroupId,
    int GroupNumber,
    int StageSlotId,
    string StageName,
    // The promotion the placement belongs to. Not decoration: a legacy group row is shared by every
    // level that used its number, so the stage the group is busy in often belongs to another year of
    // study entirely, and naming only the stage makes the refusal unreadable.
    string LevelLabel,
    int PeriodNumber,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// "No academic group may be in two services at the same time" — the rule that actually prevents
/// double-booking, checked where a group is really placed rather than where a date column is merely
/// declared.
///
/// This is what <see cref="SlotOverlapGuard"/> used to approximate by comparing windows across a whole
/// level. That approximation was both too strict and too weak. Too strict: it forbade the faculty's
/// own layout, where Médecine P1 and Chirurgie P1 are the same dates with partition A in one and
/// partition B in the other — nobody is double-booked because the partitions are disjoint. Too weak:
/// comparing windows alone can never see *which* group is in them, so it could not distinguish that
/// legitimate case from group 1 being placed in both.
///
/// A group's occupied windows are the slots assigned to <b>any</b> of its cohorts — a group has one
/// cohort per stage, so the conflict is naturally cross-stage. The year needs no scoping of its own:
/// a group belongs to exactly one.
/// </summary>
internal sealed class GroupScheduleConflictGuard(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Everywhere <paramref name="academicGroupIds"/> are currently placed, minus the slots in
    /// <paramref name="ignoredSlotIds"/> — the ones a caller is about to rewrite, which must not
    /// conflict with themselves.
    /// </summary>
    public async Task<GroupOccupancy> BuildAsync(
        IReadOnlyCollection<int> academicGroupIds,
        IReadOnlyCollection<int> ignoredSlotIds,
        CancellationToken ct)
    {
        if (academicGroupIds.Count == 0)
            return new GroupOccupancy([]);

        var placements = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => academicGroupIds.Contains(a.Cohort.AcademicGroupId))
            .Where(a => !ignoredSlotIds.Contains(a.StageSlotId))
            .Select(a => new GroupPlacement(
                a.Cohort.AcademicGroupId,
                a.Cohort.AcademicGroup.GroupNumber,
                a.StageSlotId,
                a.StageSlot.Stage.Name,
                a.StageSlot.Stage.Level.Label ?? ("niveau " + a.StageSlot.Stage.LevelId),
                a.StageSlot.PeriodNumber,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate))
            .ToListAsync(ct);

        return new GroupOccupancy(placements);
    }

    /// <summary>
    /// Refuses moving a slot's dates when a group already placed in it would then sit in two
    /// services at once.
    ///
    /// Assigning a cohort is not the only way to create the clash: dragging an existing period onto
    /// another stage's window does it to every group already in the period, without touching a
    /// single <c>CohortSlotAssignment</c>. <see cref="SlotOverlapGuard"/> no longer catches this —
    /// it only compares periods of the same stage — so the update path has to ask directly.
    /// </summary>
    public async Task<Result> EnsureSlotCanMoveAsync(
        int stageSlotId, DateOnly newStart, DateOnly newEnd, CancellationToken ct)
    {
        var groupIds = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.StageSlotId == stageSlotId)
            .Select(a => a.Cohort.AcademicGroupId)
            .Distinct()
            .ToListAsync(ct);

        if (groupIds.Count == 0)
            return Result.Success();

        var occupancy = await BuildAsync(groupIds, [stageSlotId], ct);

        foreach (int groupId in groupIds)
        {
            if (occupancy.ConflictFor(groupId, newStart, newEnd) is { } conflict)
            {
                return Result.Failure(StageErrors.GroupAlreadyPlaced(
                    conflict.GroupNumber, conflict.StageName, conflict.LevelLabel,
                    conflict.PeriodNumber, conflict.StartDate, conflict.EndDate));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Refuses placing one cohort in one slot when its group is already busy over those dates.
    /// The single-cell path, for a manual edit of the grid.
    /// </summary>
    public async Task<Result> EnsureGroupIsFreeAsync(int cohortId, int stageSlotId, CancellationToken ct)
    {
        var target = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.Id == stageSlotId)
            .Select(s => new { s.StartDate, s.EndDate })
            .FirstOrDefaultAsync(ct);

        if (target is null)
            return Result.Failure(StageErrors.SlotNotFound(stageSlotId));

        int groupId = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => c.AcademicGroupId)
            .FirstOrDefaultAsync(ct);

        if (groupId == 0)
            return Result.Failure(StageErrors.CohortNotFound(cohortId));

        var occupancy = await BuildAsync([groupId], [stageSlotId], ct);
        var conflict = occupancy.ConflictFor(groupId, target.StartDate, target.EndDate);

        return conflict is null
            ? Result.Success()
            : Result.Failure(StageErrors.GroupAlreadyPlaced(
                conflict.GroupNumber, conflict.StageName, conflict.LevelLabel,
                conflict.PeriodNumber, conflict.StartDate, conflict.EndDate));
    }
}

/// <summary>A snapshot of where groups sit, queried once and then asked in memory.</summary>
public sealed class GroupOccupancy(IReadOnlyList<GroupPlacement> placements)
{
    private readonly ILookup<int, GroupPlacement> _byGroup =
        placements.ToLookup(p => p.AcademicGroupId);

    /// <summary>The first placement of <paramref name="academicGroupId"/> overlapping the window, if any.</summary>
    public GroupPlacement? ConflictFor(int academicGroupId, DateOnly start, DateOnly end) =>
        _byGroup[academicGroupId].FirstOrDefault(p => p.StartDate <= end && start <= p.EndDate);
}
