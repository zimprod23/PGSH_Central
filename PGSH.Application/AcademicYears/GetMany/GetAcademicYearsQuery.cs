using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicYears.GetMany;

public sealed record GetAcademicYearsQuery : IQuery<List<AcademicYearResponse>>;
