using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.AllowedServices;

/// <summary>
/// Declares how one authorised service of a stage is filled: by the rotation, or held for named
/// rosters and filled only by a pinned cell.
/// </summary>
/// <remarks>
/// <para><b>What it is for.</b> A partner hospital — the GST services at Kénitra, the HMIMV — takes
/// a list of students the faculty names, and takes <i>only</i> them. Until this existed the claim
/// could not be made: a service has to be in the stage's allowed list for
/// <c>SetCohortSlotAssignment</c> to accept a pin, and being in that list is precisely what puts it
/// in <c>RotationArranger</c>'s pool. So the run that rebalanced everyone else placed other cohorts
/// on top of the named ones — and did not notice, because <c>saturatedServices</c> is computed after
/// the save, as a report, while the placement weights by capacity and never reads live occupancy.</para>
///
/// <para>⚠ <b>It changes nothing already written</b>, exactly like
/// <see cref="SetAllowedServiceOrderCommand"/>. Cells already on a service being reserved stay where
/// they are; the next arrange is the act that reads this. That is deliberate — a mode change is not
/// a licence to move students who are already placed, and the cells it would have to rewrite may be
/// published.</para>
///
/// <para>⚠ <b>Reserving does not pin anything either.</b> The two halves are separate acts on
/// purpose: this one says « the rotation keeps out », the pin says « these cohorts go in ». A
/// service reserved and never pinned simply stands empty, which is visible on the grid and is a
/// state somebody can correct — where a reservation that quietly pinned would be a placement nobody
/// authored.</para>
/// </remarks>
public sealed record SetAllowedServicePlacementModeCommand(
    int StageId,
    int ServiceId,
    ServicePlacementMode PlacementMode) : ICommand, IAuditableCommand
{
    public string AuditAction => "STAGE_SERVICE_PLACEMENT_MODE_SET";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("serviceId", ServiceId),
        ("placementMode", PlacementMode.ToString()));
}

internal sealed class SetAllowedServicePlacementModeCommandValidator
    : AbstractValidator<SetAllowedServicePlacementModeCommand>
{
    public SetAllowedServicePlacementModeCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.PlacementMode).IsInEnum();
    }
}
