using FluentValidation;
using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.AllowedServices;

/// <summary>
/// Authors the order the stage's services are walked in when a rotation is arranged — the whole
/// list, in the order wanted, never a single move.
///
/// <para>⚠ <b>This is a planning input.</b> <c>RotationArranger</c> emits each service's block of
/// the queue consecutively and the earliest column takes phase 0, so the service placed first
/// receives the first run of group numbers in the first période. It is what lets a nominative
/// placement — « ces étudiants au HMIMV pour Chirurgie » — fall out of the plan, instead of being a
/// cell edited by hand afterwards: the printed répartition shows such an edit, because
/// <c>GroupNumberRanges</c> refuses to merge across the hole it leaves (« 21-23, 25-27 » beside a
/// lone « 24 » in another row).</para>
///
/// <para>It changes nothing already written. Cells are rewritten by the next auto-arrange, which is
/// the act that reads this order — and that act is guarded, audited and refuses published cells on
/// its own terms.</para>
/// </summary>
public sealed record SetAllowedServiceOrderCommand(
    int StageId,
    IReadOnlyList<int> ServiceIdsInOrder) : ICommand, IAuditableCommand
{
    public string AuditAction => "STAGE_SERVICE_ORDER_SET";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("serviceCount", ServiceIdsInOrder.Count),
        ("order", string.Join(",", ServiceIdsInOrder)));
}

internal sealed class SetAllowedServiceOrderCommandValidator
    : AbstractValidator<SetAllowedServiceOrderCommand>
{
    public SetAllowedServiceOrderCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);

        // Only that the payload is well-formed. Whether it names exactly the stage's own services is
        // a question about rows in the store, so it belongs to the handler — and its refusal has to
        // name which of the three ways it can be wrong, because they call for different acts.
        RuleFor(x => x.ServiceIdsInOrder).NotNull();
        RuleForEach(x => x.ServiceIdsInOrder).GreaterThan(0);
    }
}
