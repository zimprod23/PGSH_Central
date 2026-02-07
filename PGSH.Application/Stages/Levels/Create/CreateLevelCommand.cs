using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Levels.Create;

public sealed record CreateLevelCommand(
    string Label,
    int Year,
    int AcademicProgram): ICommand<int>;
