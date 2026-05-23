using PGSH.Domain.Employees;
using PGSH.Domain.Users;

namespace PGSH.Application.Employees;

public sealed record EmployeeSummaryResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PPR,
    Grade Grade,
    Position? Position,
    WorkPlace? WorkPlace);

public sealed record EmployeeResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? CIN,
    Gender Gender,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? FullAddress,
    Grade Grade,
    Position? Position,
    WorkPlace? WorkPlace,
    string? PPR,
    string? Label,
    DateOnly? PvSignatureDate,
    IReadOnlyList<EmployeeServiceAssignment> Services);

public sealed record EmployeeServiceAssignment(
    int ServiceId,
    string ServiceName,
    string HospitalName,
    bool IsChef);
