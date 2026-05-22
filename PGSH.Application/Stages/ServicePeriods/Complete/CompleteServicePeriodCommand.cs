using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.ServicePeriods.Complete;

public sealed record CompleteServicePeriodCommand(Guid PeriodId) : ICommand;
