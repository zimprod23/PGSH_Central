namespace PGSH.Domain.Stages;

/// <summary>
/// One période of a rotation a person declared rather than the grid produced — what
/// <see cref="InternshipAssignment.DeclareRotation"/> is handed.
/// </summary>
/// <remarks>
/// ⚠ <b>Deliberately only a service and a window.</b> The lifecycle flags are not the declarer's to
/// set: a declared rotation is a plan like any other and is started by an admin, so a canvas cannot
/// hand the faculty a stage that is already « terminé » and therefore evaluable without anyone having
/// stood in it. The one exception is a délocalisation, which is served before it is recorded and goes
/// through <see cref="InternshipAssignment.Delocalize"/> instead — a different act, with a motif.
/// </remarks>
public readonly record struct DeclaredPeriod(int ServiceId, DateOnly StartDate, DateOnly EndDate);
