using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Targeting;

/// <summary>
/// The mandatory dry run. Which CNPN a student follows decides how many years they owe and which
/// stages count, so nothing is stamped until someone has seen the population and the exceptions.
/// </summary>
public sealed record PreviewCnpnTargetQuery(int CnpnVersionId, CnpnTargetCriteria Criteria)
    : IQuery<CnpnTargetPreview>;

internal sealed class PreviewCnpnTargetQueryHandler(
    CnpnTargetPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewCnpnTargetQuery, CnpnTargetPreview>
{
    public async Task<Result<CnpnTargetPreview>> Handle(
        PreviewCnpnTargetQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure)
            return Result.Failure<CnpnTargetPreview>(access.Error);

        var plan = await planner.PlanAsync(request.CnpnVersionId, request.Criteria, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<CnpnTargetPreview>(plan.Error)
            : plan.Value.Preview;
    }
}
