using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.PublishRotation;

public sealed record PublishRotationCommand(int CohortId) : ICommand<int>;
