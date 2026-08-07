using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.Import;

/// <summary>
/// The mandatory dry run. Grades are the highest-consequence data in the system, so nothing is
/// written until someone has seen, row by row, what the sheet would do.
/// </summary>
public sealed record PreviewEvaluationImportQuery(
    int StageId,
    EvaluationImportScope Scope,
    int? PeriodNumber,
    EvaluationMode Mode,
    IReadOnlyList<EvaluationImportRow> Rows) : IQuery<EvaluationImportReport>;

internal sealed class PreviewEvaluationImportQueryHandler(EvaluationImportPlanner planner)
    : IQueryHandler<PreviewEvaluationImportQuery, EvaluationImportReport>
{
    public async Task<Result<EvaluationImportReport>> Handle(
        PreviewEvaluationImportQuery request, CancellationToken cancellationToken)
    {
        var plan = await planner.PlanAsync(
            request.StageId, request.Scope, request.PeriodNumber, request.Mode, request.Rows,
            cancellationToken);

        return plan.IsFailure
            ? Result.Failure<EvaluationImportReport>(plan.Error)
            : plan.Value.Report;
    }
}
