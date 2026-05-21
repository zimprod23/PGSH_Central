namespace PGSH.Application.AcademicYears.GetMany;

public sealed record AcademicYearResponse(int Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);
