using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// "Is this planning cell published?" — asked by everything that wants to rewrite, clear or delete
/// part of the grid, and the one question that must <b>not</b> be answered from
/// <c>ServicePeriod.CohortSlotAssignmentId</c>.
///
/// <para>⚠ That FK names the <i>first</i> cell a period came from. Under
/// <see cref="Domain.Stages.StageRotationMode.SingleService"/> one period covers a whole run, so
/// reading the FK would report the lead cell locked and every trailing cell of the run free — and the
/// arranger would then rewrite them, or <c>DeleteStageSlot</c> would drop a column out from under a
/// running stage. <c>ServicePeriodSlotCoverage</c> holds one row per covered cell under both modes,
/// which makes it the same answer in the simple case and the correct one in the other.</para>
///
/// <para>Gathered here so the four callers cannot drift: <c>RotationArranger</c> (which cells it may
/// overwrite), <c>DeleteStageSlotCommandHandler</c>, <c>ClearCohortSlotAssignmentCommandHandler</c>
/// and <c>ClearSlotAssignmentsCommandHandler</c>.</para>
/// </summary>
internal static class PublishedCells
{
    /// <summary>The subset of <paramref name="cellIds"/> that a service period already covers.</summary>
    public static async Task<HashSet<int>> PublishedAmongAsync(
        this IApplicationDbContext dbContext, IReadOnlyCollection<int> cellIds, CancellationToken ct)
    {
        if (cellIds.Count == 0)
            return [];

        var covered = await dbContext.ServicePeriodSlotCoverage
            .Where(c => cellIds.Contains(c.CohortSlotAssignmentId))
            .Select(c => c.CohortSlotAssignmentId)
            .Distinct()
            .ToListAsync(ct);

        return covered.ToHashSet();
    }

    public static Task<bool> IsCellPublishedAsync(
        this IApplicationDbContext dbContext, int cellId, CancellationToken ct) =>
        dbContext.ServicePeriodSlotCoverage.AnyAsync(c => c.CohortSlotAssignmentId == cellId, ct);

    public static Task<bool> SlotHasPublishedCellAsync(
        this IApplicationDbContext dbContext, int slotId, CancellationToken ct) =>
        dbContext.ServicePeriodSlotCoverage
            .AnyAsync(c => c.CohortSlotAssignment.StageSlotId == slotId, ct);
}
