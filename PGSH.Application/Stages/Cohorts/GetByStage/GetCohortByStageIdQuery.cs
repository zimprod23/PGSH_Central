using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cohorts.GetById;

namespace PGSH.Application.Stages.Cohorts.GetByStage;

public sealed record GetCohortsByStageQuery(int StageId) : IQuery<IReadOnlyCollection<CohortResponse>>;