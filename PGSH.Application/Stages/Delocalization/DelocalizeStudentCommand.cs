using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// Records that one student served a whole stage outside the faculty.
/// </summary>
/// <param name="StartDate">
/// Omitted, the stage's own window for this student's promotion is used. The external hospital does
/// not follow our calendar, so these dates are the faculty's statement of when the stage took place,
/// not a schedule anyone enforced — see <see cref="DelocalizationWindow"/>.
/// </param>
/// <param name="Verdict">
/// The paper validation, when it is already in hand. Null records the movement now and leaves the
/// verdict to be entered later — by hand, or in bulk through the évaluation canvas, which reaches a
/// délocalisation like any other closed rotation.
/// </param>
public sealed record DelocalizeStudentCommand(
    Guid                  RegistrationId,
    int                   StageId,
    int                   ServiceId,
    string                Reason,
    DateOnly?             StartDate = null,
    DateOnly?             EndDate   = null,
    DelocalizationVerdict? Verdict  = null,
    Guid?                 DemandeId = null)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "STUDENT_DELOCALIZED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();
    public string? AuditMetadata   =>
        $"{{\"stageId\":{StageId},\"serviceId\":{ServiceId},\"reason\":\"{Reason}\"}}";
}
