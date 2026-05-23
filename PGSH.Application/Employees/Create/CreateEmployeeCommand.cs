using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.Domain.Users;

namespace PGSH.Application.Employees.Create;

public sealed record CreateEmployeeCommand(
    string Email,
    string FirstName,
    string LastName,
    string? CIN,
    string? PPR,
    string? Label,
    Grade Grade,
    Position? Position,
    WorkPlace? WorkPlace,
    Gender Gender,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? FullAddress,
    DateOnly? PvSignatureDate) : ICommand<Guid>;
