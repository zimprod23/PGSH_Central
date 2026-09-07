using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;
using PGSH.Application.Students.Selection;

namespace PGSH.Application.Stages.Delocalization.Bulk;

/// <summary>
/// What délocalising this selection would do. Nothing is written; the report <em>is</em> the plan the
/// apply executes, because both run <see cref="BulkDelocalizationPlanner"/> and nothing else.
/// </summary>
public sealed record PreviewBulkDelocalizationQuery(
    int StageId,
    int ServiceId,
    StudentTargets Targets,
    int? AcademicYearId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : IQuery<BulkDelocalizationReport>;

internal sealed class PreviewBulkDelocalizationQueryValidator
    : AbstractValidator<PreviewBulkDelocalizationQuery>
{
    public PreviewBulkDelocalizationQueryValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.Targets).NotNull();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");
    }
}

internal sealed class PreviewBulkDelocalizationQueryHandler(
    BulkDelocalizationPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewBulkDelocalizationQuery, BulkDelocalizationReport>
{
    public async Task<Result<BulkDelocalizationReport>> Handle(
        PreviewBulkDelocalizationQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BulkDelocalizationErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BulkDelocalizationReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.StageId, request.ServiceId, request.AcademicYearId, request.Targets,
            request.StartDate, request.EndDate, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<BulkDelocalizationReport>(plan.Error)
            : plan.Value.Report;
    }
}
