using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.AllowedServices;

public sealed record AddAllowedServiceCommand(int StageId, int ServiceId) : ICommand;

public sealed record RemoveAllowedServiceCommand(int StageId, int ServiceId) : ICommand;
