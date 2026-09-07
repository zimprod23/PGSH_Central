namespace PGSH.Domain.Stages;

/// <summary>
/// How a service authorised for a stage is filled: by the rotation, or by hand only.
/// </summary>
/// <remarks>
/// <para>⚠ <b>« Ce service est réservé à ces étudiants » had no way of being said.</b> A service must
/// be in a stage's allowed list for <c>SetCohortSlotAssignment</c> to accept a pin — and being in
/// that list is exactly what puts it in <c>RotationArranger</c>'s pool. So the run that rebalanced
/// everyone else placed other cohorts on top of the named ones, and did not notice:
/// <c>saturatedServices</c> is computed <i>after</i> <c>SaveChangesAsync</c>, as a report, while the
/// placement itself weights by <c>CapacityFor(levelId)</c> and never reads live occupancy.</para>
///
/// <para>This is a property of the <b>authorisation</b> — the (stage, service) pair — because that is
/// already where « may this service host this stage » is answered, and because both sides are
/// year-invariant catalogue. ⚠ An FK to the roster it is held for would put a year-constituted
/// reference on a year-invariant row, which is the boundary the whole codebase is careful about;
/// <i>who</i> stands in a reserved service is a fact of the pinned cells, and those carry the year
/// through their cohorte.</para>
/// </remarks>
public enum ServicePlacementMode
{
    /// <summary>
    /// The rotation may place any cohort here. The default, so authorising a service keeps meaning
    /// exactly what it meant before this column existed.
    /// </summary>
    Rotation = 0,

    /// <summary>
    /// Held for named rosters: the arranger never chooses it, and only a pinned cell puts anybody
    /// there. ⚠ Its capacity leaves <c>totalCapacity</c> with it, so the caller must say how many
    /// services were withheld — a promotion losing places in silence is the defect this codebase
    /// calls « say what a blank means ».
    /// </summary>
    Reserved = 1,
}
