using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// What the faculty's réinscription roll would do. Nothing is written; the report <em>is</em> the
/// plan the apply executes, because both run <see cref="ReinscriptionSheetPlanner"/> and nothing else.
/// </summary>
public sealed record PreviewReinscriptionSheetQuery(
    IReadOnlyList<ReinscriptionSheetRow> Rows,
    int? FromAcademicYearId,
    int? ToAcademicYearId) : IQuery<ReinscriptionSheetReport>;

internal sealed class PreviewReinscriptionSheetQueryValidator
    : AbstractValidator<PreviewReinscriptionSheetQuery>
{
    public PreviewReinscriptionSheetQueryValidator()
    {
        RuleFor(x => x.FromAcademicYearId)
            .IsARequiredReference(ReinscriptionSheetErrors.FromYearRequiredMessage);
        RuleFor(x => x.ToAcademicYearId)
            .IsARequiredReference(ReinscriptionSheetErrors.ToYearRequiredMessage);
    }
}

internal sealed class PreviewReinscriptionSheetQueryHandler(
    ReinscriptionSheetPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewReinscriptionSheetQuery, ReinscriptionSheetReport>
{
    public async Task<Result<ReinscriptionSheetReport>> Handle(
        PreviewReinscriptionSheetQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ReinscriptionSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReinscriptionSheetReport>(access.Error);

        // Nullable only so an omitted query-string year reaches the validator instead of
        // throwing in routing; both have been refused in words by the time we are here.
        var plan = await planner.PlanAsync(
            request.FromAcademicYearId!.Value, request.ToAcademicYearId!.Value, request.Rows,
            cancellationToken);

        return plan.IsFailure
            ? Result.Failure<ReinscriptionSheetReport>(plan.Error)
            : plan.Value.Report;
    }
}
