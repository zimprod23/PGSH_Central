using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.GetById;

public record GetCohortByIdQuery(int Id) : IQuery<CohortResponse>;
