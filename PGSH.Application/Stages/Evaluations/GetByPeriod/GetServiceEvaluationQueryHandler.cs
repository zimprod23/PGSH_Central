using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations.GetByPeriod;

internal sealed class GetServiceEvaluationQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetServiceEvaluationQuery, ServiceEvaluationResponse>
{
    public async Task<Result<ServiceEvaluationResponse>> Handle(
        GetServiceEvaluationQuery request, CancellationToken cancellationToken)
    {
        var evaluation = await dbContext.ServiceEvaluation
            .AsNoTracking()
            .Where(e => e.ServicePeriodId == request.ServicePeriodId)
            .Select(e => new ServiceEvaluationResponse(
                e.Id,
                e.ServicePeriodId,
                e.Mode,
                e.TotalScore,
                e.Outcome,
                e.SupervisorComment,
                e.ObjectiveScores
                    .OrderBy(o => o.StageObjective.Weight)
                    .Select(o => new ObjectiveScoreResponse(
                        o.Id,
                        o.StageObjectiveId,
                        o.StageObjective.Label,
                        o.StageObjective.Weight,
                        o.StageObjective.IsMandatory,
                        o.Score,
                        o.Outcome,
                        o.Note))
                    .ToList(),
                e.FicheReference,
                dbContext.Users
                    .Where(u => u.Id == e.EvaluatedByUserId)
                    .Select(u => (u.FirstName ?? "") + " " + (u.LastName ?? ""))
                    .FirstOrDefault(),
                e.EvaluatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return evaluation is null
            ? Result.Failure<ServiceEvaluationResponse>(StageErrors.EvaluationNotFound(request.ServicePeriodId))
            : evaluation;
    }
}
