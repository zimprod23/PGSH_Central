using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Import;

public sealed record ImportEvaluationsCommand(
    int StageId,
    EvaluationImportScope Scope,
    int? PeriodNumber,
    EvaluationMode Mode,
    IReadOnlyList<EvaluationImportRow> Rows) : ICommand<EvaluationImportReport>;

internal sealed class ImportEvaluationsCommandValidator : AbstractValidator<ImportEvaluationsCommand>
{
    public ImportEvaluationsCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Le fichier ne contient aucune ligne.");

        RuleFor(x => x.PeriodNumber)
            .NotNull().GreaterThan(0)
            .When(x => x.Scope == EvaluationImportScope.SinglePeriod)
            .WithMessage("Indiquez la période visée pour un import par période.");

        // A sheet carries one verdict per student; per-objective marks do not fit on a line.
        RuleFor(x => x.Mode)
            .NotEqual(EvaluationMode.ValidateObjectives)
            .WithMessage(StageErrors.ImportModeNotSupported.Description);
    }
}

/// <summary>
/// Applies a previewed sheet. Runs the same planner the preview ran, refuses outright if anything at
/// all is wrong, and writes every mark through the aggregate — the same path a chef's single
/// evaluation takes, so scoring, the stage roll-up and the domain events stay identical. One
/// SaveChanges, so the whole import lands or none of it does.
/// </summary>
internal sealed class ImportEvaluationsCommandHandler(
    IApplicationDbContext dbContext,
    EvaluationImportPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ImportEvaluationsCommand, EvaluationImportReport>
{
    public async Task<Result<EvaluationImportReport>> Handle(
        ImportEvaluationsCommand request, CancellationToken cancellationToken)
    {
        var plan = await planner.PlanAsync(
            request.StageId, request.Scope, request.PeriodNumber, request.Mode, request.Rows,
            cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<EvaluationImportReport>(plan.Error);

        var report = plan.Value.Report;
        if (!report.CanApply)
            return Result.Failure<EvaluationImportReport>(StageErrors.ImportRejected(report.ErrorCount));

        var evaluatedBy = await authorizer.CurrentUserIdAsync(cancellationToken);
        var evaluatedAt = DateTime.UtcNow;

        foreach (var item in plan.Value.Work)
        {
            var result = item.Period.Evaluation is null
                ? item.Assignment.SubmitEvaluation(item.Period.Id, new ServiceEvaluation
                {
                    ServicePeriodId   = item.Period.Id,
                    Mode              = item.Mode,
                    TotalScore        = item.Mark,
                    Outcome           = item.Outcome,
                    SupervisorComment = item.Comment,
                    EvaluatedByUserId = evaluatedBy,
                    EvaluatedAt       = evaluatedAt,
                })
                : item.Assignment.AmendEvaluation(item.Period.Id, new EvaluationAmendment(
                    item.Mode, item.Mark, item.Outcome, item.Comment,
                    FicheReference: item.Period.Evaluation.FicheReference,
                    evaluatedBy, evaluatedAt, ObjectiveScores: []));

            // The planner already cleared every guard these can return, so a failure here means the
            // plan and the aggregate disagree — refuse the batch rather than write part of it.
            if (result.IsFailure)
                return Result.Failure<EvaluationImportReport>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return report;
    }
}
