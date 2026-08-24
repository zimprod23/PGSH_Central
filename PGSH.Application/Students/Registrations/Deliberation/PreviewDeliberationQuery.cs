using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// The mandatory dry run. Nothing is written; the report it returns is the plan the apply will
/// execute, row for row — including the verdicts it derives from silence.
/// </summary>
public sealed record PreviewDeliberationQuery(
    IReadOnlyList<DeliberationRow> Rows,
    int? LevelId = null,
    int? AcademicYearId = null,
    bool DefaultUnlistedToAdmis = false) : IQuery<DeliberationReport>
{
    public DeliberationScope Scope => new(LevelId, AcademicYearId, DefaultUnlistedToAdmis);
}

internal sealed class PreviewDeliberationQueryValidator : AbstractValidator<PreviewDeliberationQuery>
{
    public PreviewDeliberationQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0).When(x => x.LevelId is not null);

        // ⚠ Empty is refused even in exceptions mode, where "nobody failed" is a real situation. A
        // workbook whose headers did not match parses to zero rows too, and under a default of
        // « Admis » that is indistinguishable from a file saying "promote everyone".
        RuleFor(x => x.Rows).NotEmpty().WithMessage(DeliberationErrors.EmptySheetMessage);
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

        var plan = await planner.PlanAsync(request.Scope, request.Rows, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<DeliberationReport>(plan.Error)
            : plan.Value.Report;
    }
}
