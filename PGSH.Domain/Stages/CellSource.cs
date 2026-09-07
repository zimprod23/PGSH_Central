namespace PGSH.Domain.Stages;

/// <summary>
/// Who decided a cell of the planning grid — the rotation engine, or a human.
/// </summary>
/// <remarks>
/// <para>⚠ <b>It exists because the arranger cannot otherwise tell the two apart.</b>
/// <c>RotationArranger</c> deletes and rewrites every unpublished cell within its reach
/// (<c>staleIds</c>), so a placement somebody chose by hand — the whole substance of a nominative
/// request, « ces volontaires à Kénitra » — was destroyed by the next « auto-répartir ce stage »
/// with no refusal, no count, and an <c>Assigned = N</c> that looked entirely normal.</para>
///
/// <para>A <see cref="Pinned"/> cell is treated by the arranger exactly as a published one: never
/// deleted, never rewritten, and its column position excluded from the balance so the remaining
/// cohorts are spread over what is actually left. It is <b>counted</b> in the result
/// (<c>PinnedCellsKept</c>) for the same reason <c>SkippedAlreadyServed</c> is: an act that leaves
/// rows alone must say how many, or a run that placed far fewer cohorts than expected looks like a
/// run that failed.</para>
///
/// <para>The difference from publication is that a pin is a <i>plan</i>, not an execution record: it
/// can still be cleared or moved by hand, and unlike a published cell it carries no période behind
/// it. That is why this is a separate column rather than an inference from
/// <c>PublishedAmongAsync</c>.</para>
/// </remarks>
public enum CellSource
{
    /// <summary>Written by <c>RotationArranger</c>. The default, so every existing row keeps its meaning.</summary>
    Arranged = 0,

    /// <summary>Chosen by a human through <c>SetCohortSlotAssignment</c>. The arranger leaves it alone.</summary>
    Pinned = 1,
}
