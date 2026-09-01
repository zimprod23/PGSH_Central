using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// The mandatory dry run. Nothing is written; the report it returns is the plan the apply will
/// execute, row for row — including which rows create a person and which addresses PGSH would
/// manufacture for them.
/// </summary>
public sealed record PreviewInscriptionQuery(
    IReadOnlyList<InscriptionRow> Rows,
    int LevelId,
    int? AcademicYearId = null) : IQuery<InscriptionReport>
{
    public InscriptionScope Scope => new(LevelId, AcademicYearId);
}

internal sealed class PreviewInscriptionQueryValidator : AbstractValidator<PreviewInscriptionQuery>
{
    public PreviewInscriptionQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Rows).NotEmpty().WithMessage(InscriptionErrors.EmptySheetMessage);
    }
}

internal sealed class PreviewInscriptionQueryHandler(
    InscriptionPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewInscriptionQuery, InscriptionReport>
{
    public async Task<Result<InscriptionReport>> Handle(
        PreviewInscriptionQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(InscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<InscriptionReport>(access.Error);

        var plan = await planner.PlanAsync(request.Scope, request.Rows, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<InscriptionReport>(plan.Error)
            : plan.Value.Report;
    }
}
