using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Revalidation;

/// <summary>
/// Re-opens a stage the student failed under an earlier registration, on the registration they hold
/// now. Produces a fresh <c>InternshipAssignment</c> — the failed one is history and stays untouched.
///
/// <para>
/// A revalidation is served <b>where the student failed it</b>, not wherever this year's planning grid
/// would send their group: leave <paramref name="ServiceId"/> null and the service of the failed
/// rotation is reused. It is therefore an ad-hoc placement like a délocalisation, outside the published
/// schedule (<c>CohortSlotAssignmentId</c> stays null), not a slot in the group's rotation.
/// </para>
/// </summary>
/// <param name="ServiceId">
/// Overrides the service to serve the retake in. Per the faculty's rule a change of service is itself
/// subject to an approved demande, so this should not be set without one — recorded here via
/// <paramref name="DemandeId"/> until the Demande service exists (Phase 5) to enforce it.
/// </param>
/// <param name="StartDate">
/// Placement window. Supply both dates with the service to place the retake immediately; leave all
/// three null to create the assignment now and schedule it later.
/// </param>
/// <param name="DemandeId">
/// The demande this revalidation answers. Revalidation is never automatic — it starts from a student's
/// request. Only <c>Delocalization</c> has a column for this today, so until the Demande service exists
/// (Phase 5) the reference is kept in the audit entry rather than dropped; a revalidation needs its own
/// column when that lands.
/// </param>
public sealed record RevalidateStageCommand(
    Guid      RegistrationId,
    int       StageId,
    int?      CohortId  = null,
    int?      ServiceId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate   = null,
    string?   Reason    = null,
    Guid?     DemandeId = null)
    : ICommand<Guid>, IAuditableCommand
{
    /// <summary>True when the caller asked for the retake to be placed straight away.</summary>
    public bool PlacesRotation => StartDate is not null || EndDate is not null || ServiceId is not null;

    public string  AuditAction     => "STAGE_REVALIDATION_OPENED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();

    // The demande has no column of its own yet, so the audit entry is where the link survives.
    public string? AuditMetadata   =>
        $"{{\"stageId\":{StageId},\"serviceId\":{ServiceId?.ToString() ?? "null"}," +
        $"\"demandeId\":{(DemandeId is null ? "null" : $"\"{DemandeId}\"")}}}";
}
