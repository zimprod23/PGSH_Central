using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Levels.Delete;

public sealed record DeleteLevelCommand(int Id) : ICommand;
