using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// What the rollover would create. Nothing is written; the report is the plan the apply will execute.
/// </summary>
/// <param name="LevelId">One promotion, or every promotion of the closing year when omitted.</param>
public sealed record PreviewReinscriptionQuery(
    int FromAcademicYearId,
    int ToAcademicYearId,
    int? LevelId = null) : IQuery<ReinscriptionReport>;

internal sealed class PreviewReinscriptionQueryValidator : AbstractValidator<PreviewReinscriptionQuery>
{
    public PreviewReinscriptionQueryValidator()
    {
        RuleFor(x => x.FromAcademicYearId).GreaterThan(0);
        RuleFor(x => x.ToAcademicYearId).GreaterThan(0);
        RuleFor(x => x.LevelId).GreaterThan(0).When(x => x.LevelId is not null);
    }
}

internal sealed class PreviewReinscriptionQueryHandler(
    ReinscriptionPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewReinscriptionQuery, ReinscriptionReport>
{
    public async Task<Result<ReinscriptionReport>> Handle(
        PreviewReinscriptionQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ReinscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReinscriptionReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.FromAcademicYearId, request.ToAcademicYearId, request.LevelId, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<ReinscriptionReport>(plan.Error)
            : plan.Value.Report;
    }
}
