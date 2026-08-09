using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// The mandatory dry run. Nothing is written; the report it returns is the plan the apply will
/// execute, row for row.
/// </summary>
public sealed record PreviewDeliberationQuery(
    int LevelId,
    IReadOnlyList<DeliberationRow> Rows,
    int? AcademicYearId = null) : IQuery<DeliberationReport>;

internal sealed class PreviewDeliberationQueryValidator : AbstractValidator<PreviewDeliberationQuery>
{
    public PreviewDeliberationQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Le fichier ne contient aucune ligne.");
    }
}

internal sealed class PreviewDeliberationQueryHandler(
    DeliberationPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewDeliberationQuery, DeliberationReport>
{
    public async Task<Result<DeliberationReport>> Handle(
        PreviewDeliberationQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(DeliberationErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<DeliberationReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.LevelId, request.AcademicYearId, request.Rows, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<DeliberationReport>(plan.Error)
            : plan.Value.Report;
    }
}
