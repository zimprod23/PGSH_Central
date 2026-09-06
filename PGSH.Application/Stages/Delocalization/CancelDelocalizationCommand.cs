using FluentValidation;
using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// Walks one délocalisation back: the student did not go after all, or the row was entered against
/// the wrong stage.
/// </summary>
/// <remarks>
/// ⚠ It exists because the bulk act does. A délocalisation applied to a whole roster lands on
/// students nobody typed the name of, so a mistake is measured in promotions — and until this
/// command there was no way back at all, in any quantity: the ad-hoc period is not a published one,
/// so <c>RemovePublishedPeriods</c> deliberately leaves it alone.
/// </remarks>
public sealed record CancelDelocalizationCommand(Guid RegistrationId, int StageId)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "DELOCALIZATION_CANCELLED";
    public string  AuditEntityType => "Registration";
    public string? AuditEntityId   => RegistrationId.ToString();
    public string? AuditMetadata   => $"{{\"stageId\":{StageId}}}";
}

internal sealed class CancelDelocalizationCommandValidator : AbstractValidator<CancelDelocalizationCommand>
{
    public CancelDelocalizationCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.StageId).GreaterThan(0);
    }
}
