using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.Domain.Users;

namespace PGSH.Application.Employees.Update;

public sealed record UpdateEmployeeCommand(
    Guid Id,
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
    DateOnly? PvSignatureDate) : ICommand;
