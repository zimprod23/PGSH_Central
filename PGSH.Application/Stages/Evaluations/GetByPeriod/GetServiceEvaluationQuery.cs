using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Evaluations.GetByPeriod;

public sealed record GetServiceEvaluationQuery(Guid ServicePeriodId) : IQuery<ServiceEvaluationResponse>;
