namespace PGSH.Domain.Stages;

/// <summary>
/// What <see cref="InternshipAssignment.Delocalize"/> would do to an assignment, read before it is
/// called. It exists so a bulk act can show its damage on the preview screen rather than discover it
/// row by row inside a transaction, and so the preview and the guard cannot drift: the counts here
/// are the same expressions the aggregate enforces.
/// </summary>
/// <param name="AlreadyDelocalized">
/// The stage is already served outside. Délocalising again replaces the ad-hoc period, which is what
/// makes a corrected list safe to re-send.
/// </param>
/// <param name="MarkedPeriods">
/// Periods carrying an evaluation. Non-zero is the one state <see cref="InternshipAssignment.Delocalize"/>
/// refuses — a mark is the only thing here that nothing puts back.
/// </param>
/// <param name="DroppedPeriods">How many periods the délocalisation deletes in total.</param>
/// <param name="UnderwayPeriods">
/// Of those, the ones that are <b>not merely planned</b> — read through
/// <c>ServicePeriodLifecycle.IsPlanned</c> rather than by restating its flags. Deleting them is
/// legitimate — the student left — but it is movement the app recorded, so the operator is told rather
/// than being allowed to assume the row cost nothing.
/// </param>
public sealed record DelocalizationPreflight(
    bool AlreadyDelocalized,
    int  MarkedPeriods,
    int  DroppedPeriods,
    int  UnderwayPeriods)
{
    /// <summary>Whether the aggregate would accept the délocalisation.</summary>
    public bool CanDelocalize => MarkedPeriods == 0;
}
