using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.Levels.Update;

public sealed record UpdateLevelCommand(
    int Id,
    string? Label,
    int Year,
    AcademicProgram AcademicProgram) : ICommand;
