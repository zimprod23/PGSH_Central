using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.Delete;

public sealed record DeleteCohortCommand(int CohortId) : ICommand;
