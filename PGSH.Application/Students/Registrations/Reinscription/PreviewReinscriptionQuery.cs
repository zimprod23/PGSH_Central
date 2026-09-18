using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// What the rollover would create. Nothing is written; the report is the plan the apply will execute.
/// </summary>
/// <param name="LevelId">One promotion, or every promotion of the closing year when omitted.</param>
public sealed record PreviewReinscriptionQuery(
    int? FromAcademicYearId,
    int? ToAcademicYearId,
    int? LevelId = null) : IQuery<ReinscriptionReport>;

internal sealed class PreviewReinscriptionQueryValidator : AbstractValidator<PreviewReinscriptionQuery>
{
    public PreviewReinscriptionQueryValidator()
    {
        RuleFor(x => x.FromAcademicYearId)
            .IsARequiredReference(ReinscriptionErrors.FromYearRequiredMessage);
        RuleFor(x => x.ToAcademicYearId)
            .IsARequiredReference(ReinscriptionErrors.ToYearRequiredMessage);
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

        // Nullable only so an omitted query-string year reaches the validator instead of
        // throwing in routing; both have been refused in words by the time we are here.
        var plan = await planner.PlanAsync(
            request.FromAcademicYearId!.Value, request.ToAcademicYearId!.Value, request.LevelId,
            cancellationToken);

        return plan.IsFailure
            ? Result.Failure<ReinscriptionReport>(plan.Error)
            : plan.Value.Report;
    }
}
