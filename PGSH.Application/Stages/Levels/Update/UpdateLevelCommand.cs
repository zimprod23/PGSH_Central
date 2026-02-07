using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Levels.Update;

public sealed record UpdateLevelCommand(
    int Id,
    string Label,
    int Year,
    int AcademicProgram): ICommand;
