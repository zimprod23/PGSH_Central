using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.Levels.Create;

public sealed record CreateLevelCommand(
    string? Label,
    int Year,
    AcademicProgram AcademicProgram) : ICommand<int>;
