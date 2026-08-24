using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Effectivity;

/// <summary>
/// What re-stamping the registrations that already exist would do. Runs the same planner the apply
/// runs, and saves nothing.
/// </summary>
public sealed record PreviewCnpnEffectivityQuery(int EffectivityId)
    : IQuery<CnpnEffectivityApplyPreview>;

/// <summary>
/// Re-stamps registrations that were created before the rule was authored.
///
/// <para><b>Not the ordinary path.</b> A rule authored before the réinscription is applied as each
/// registration is created and this command is never needed. It exists for the other order — the
/// rollover ran in September, the faculty settled the cut in October — because otherwise those
/// registrations can only be moved with SQL.</para>
///
/// <para>⚠ <b>No override flag, deliberately.</b> A year already pronounced is refused and counted,
/// never forced: the verdict was recorded against a requirement set, and moving that set afterwards
/// leaves nobody able to say what the jury ruled on. Re-opening the year is the act that makes such a
/// change legitimate, and it is a decision somebody takes by name.</para>
/// </summary>
/// <param name="ConfirmedMoveCount">
/// The number of registrations the operator was shown. Refused on a mismatch, for the reason the
/// déliberation's <c>ConfirmedDefaultCount</c> exists: a registration created between the preview and
/// the apply silently widens the act, and a boolean confirmation cannot notice.
/// </param>
public sealed record ApplyCnpnEffectivityCommand(int EffectivityId, int ConfirmedMoveCount)
    : ICommand<CnpnEffectivityApplyPreview>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_EFFECTIVITY_APPLIED";
    public string  AuditEntityType => "CnpnLevelEffectivity";
    public string? AuditEntityId   => EffectivityId.ToString();
    public string? AuditMetadata   => $$"""{"confirmedMoveCount":{{ConfirmedMoveCount}}}""";
}

internal sealed class PreviewCnpnEffectivityQueryValidator
    : AbstractValidator<PreviewCnpnEffectivityQuery>
{
    public PreviewCnpnEffectivityQueryValidator() => RuleFor(x => x.EffectivityId).GreaterThan(0);
}

internal sealed class ApplyCnpnEffectivityCommandValidator
    : AbstractValidator<ApplyCnpnEffectivityCommand>
{
    public ApplyCnpnEffectivityCommandValidator()
    {
        RuleFor(x => x.EffectivityId).GreaterThan(0);
        RuleFor(x => x.ConfirmedMoveCount).GreaterThanOrEqualTo(0);
    }
}

internal sealed class PreviewCnpnEffectivityQueryHandler(
    CnpnEffectivityPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewCnpnEffectivityQuery, CnpnEffectivityApplyPreview>
{
    public async Task<Result<CnpnEffectivityApplyPreview>> Handle(
        PreviewCnpnEffectivityQuery request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<CnpnEffectivityApplyPreview>(access.Error);

        var plan = await planner.PlanAsync(request.EffectivityId, ct);
        return plan.IsFailure
            ? Result.Failure<CnpnEffectivityApplyPreview>(plan.Error)
            : plan.Value.Preview;
    }
}

internal sealed class ApplyCnpnEffectivityCommandHandler(
    IApplicationDbContext dbContext,
    CnpnEffectivityPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyCnpnEffectivityCommand, CnpnEffectivityApplyPreview>
{
    public async Task<Result<CnpnEffectivityApplyPreview>> Handle(
        ApplyCnpnEffectivityCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<CnpnEffectivityApplyPreview>(access.Error);

        var plan = await planner.PlanAsync(request.EffectivityId, ct);
        if (plan.IsFailure) return Result.Failure<CnpnEffectivityApplyPreview>(plan.Error);

        var preview = plan.Value.Preview;

        if (!preview.CanApply)
            return Result.Failure<CnpnEffectivityApplyPreview>(CnpnErrors.EffectivityNothingToApply);

        if (preview.WillMove != request.ConfirmedMoveCount)
            return Result.Failure<CnpnEffectivityApplyPreview>(
                CnpnErrors.EffectivityMoveCountNotConfirmed(request.ConfirmedMoveCount, preview.WillMove));

        var stamp = await planner.StampAsync(plan.Value.Work, ct);
        if (stamp.IsFailure) return Result.Failure<CnpnEffectivityApplyPreview>(stamp.Error);

        await dbContext.SaveChangesAsync(ct);
        return preview;
    }
}
