using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Delete;

public record DeleteStageCommand(int StageId): ICommand;
