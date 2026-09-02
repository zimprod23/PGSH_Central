using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Holds;

/// <summary>
/// One flagged registration, as the worklist shows it.
/// </summary>
/// <param name="Evidence">
/// What was true when the hold was raised, in the sentence the operator was shown at the time. ⚠ Not
/// re-derived on read: the debt may have been cleared, the stage dropped by a new text, or the
/// student re-registered since, and a flag that silently rewrites its own justification is one nobody
/// can audit. If the evidence no longer holds, that is precisely the discovery that releases it.
/// </param>
/// <param name="Remedy">What has to happen before it can be lifted, in the operator's own terms.</param>
/// <param name="BlocksPlanning">
/// Whether this flag actually withdraws the registration from planning.
///
/// <para>⚠ <b>Sent, never re-derived on the client.</b> Which reasons freeze is a domain rule
/// (<c>RegistrationHoldReasonExtensions.Blocking</c>), and a screen that decided it for itself would
/// be a second copy of that rule — free to disagree the day a reason is added. Same split as
/// <c>ServicePeriodResponse.State</c>, for the same reason.</para>
///
/// <para>It is the difference between « il est gelé » and « sa fiche est à compléter », which call
/// for different urgency: the first is holding a promotion up, the second is not holding up
/// anything.</para>
/// </param>
public sealed record RegistrationHoldResponse(
    Guid Id,
    Guid RegistrationId,
    Guid StudentId,
    string StudentFullName,
    string? Cne,
    string? Appogee,
    string LevelLabel,
    string AcademicYearLabel,
    RegistrationStatus RegistrationStatus,
    RegistrationHoldReason Reason,
    string ReasonLabel,
    string Evidence,
    string Remedy,
    bool BlocksPlanning,
    DateTime RaisedOn,
    DateTime? ReleasedOn,
    string? ReleaseNote);

/// <summary>Which holds to list. Defaults to the ones still standing, which is the worklist.</summary>
/// <remarks>
/// ⚠ « Active » means <b>unreleased</b>, not <b>blocking</b>. An advisory flag is every bit as much a
/// thing somebody has to deal with — it is simply not stopping the répartition meanwhile — so the
/// worklist shows both and marks which is which.
/// </remarks>
public enum RegistrationHoldFilter
{
    /// <summary>Unreleased — the students still frozen out of planning.</summary>
    Active,

    /// <summary>Released — the audit trail of who was cleared, on what note.</summary>
    Released,

    All,
}
