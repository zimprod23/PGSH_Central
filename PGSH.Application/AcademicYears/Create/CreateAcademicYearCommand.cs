using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicYears.Create;

public sealed record CreateAcademicYearCommand(
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrent) : ICommand<int>;
