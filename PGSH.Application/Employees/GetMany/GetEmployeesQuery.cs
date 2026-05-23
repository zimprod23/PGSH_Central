using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.GetMany;

public sealed record GetEmployeesQuery(
    string? SearchTerm,
    Grade? Grade,
    Position? Position,
    int? ServiceId,
    int? HospitalId,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResponse<EmployeeSummaryResponse>>;
