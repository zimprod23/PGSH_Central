using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>
/// What téléverser this canvas would do. Nothing is written; the report <em>is</em> the plan the apply
/// executes, because both run <see cref="AffectationSheetPlanner"/> and nothing else.
/// </summary>
/// <remarks>
/// ⚠ <b>The aperçu is not optional here and the apply does not trust its output.</b> Both re-parse the
/// uploaded file and re-run the planner: what is applied is what was uploaded, never a client
/// round-trip of rows the browser could have edited in between. The only thing that travels from one
/// call to the other is the two numbers the operator was shown.
/// </remarks>
public sealed record PreviewAffectationSheetQuery(
    IReadOnlyList<AffectationSheetRow> Rows,
    int LevelId,
    int? AcademicYearId = null) : IQuery<AffectationSheetReport>;

internal sealed class PreviewAffectationSheetQueryValidator
    : AbstractValidator<PreviewAffectationSheetQuery>
{
    public PreviewAffectationSheetQueryValidator()
    {
        RuleFor(x => x.Rows).NotNull();
        RuleFor(x => x.LevelId).GreaterThan(0);
    }
}

internal sealed class PreviewAffectationSheetQueryHandler(
    AffectationSheetPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewAffectationSheetQuery, AffectationSheetReport>
{
    public async Task<Result<AffectationSheetReport>> Handle(
        PreviewAffectationSheetQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<AffectationSheetReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.LevelId, request.AcademicYearId, request.Rows, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<AffectationSheetReport>(plan.Error)
            : plan.Value.Report;
    }
}
