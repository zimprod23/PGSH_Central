using PGSH.SharedKernel;

namespace PGSH.Domain.Employees;

public static class EmployeeErrors
{
    public static Error NotFound(Guid employeeId) => Error.NotFound(
        "Employees.NotFound",
        $"The employee with Id = '{employeeId}' was not found.");

    public static Error DuplicateEmail(string email) => Error.Conflict(
        "Employees.DuplicateEmail",
        $"A user with email '{email}' already exists.");

    public static Error DuplicatePPR(string ppr) => Error.Conflict(
        "Employees.DuplicatePPR",
        $"An employee with PPR '{ppr}' already exists.");

    public static readonly Error NotInStaff = Error.Validation(
        "Employees.NotInStaff",
        "The employee must be added to the service staff before being assigned as ServiceChef.");

    public static readonly Error WrongPosition = Error.Validation(
        "Employees.WrongPosition",
        "This employee's Position must be 'ServiceChef' to be assigned as service chief.");

    public static readonly Error AlreadyInStaff = Error.Conflict(
        "Employees.AlreadyInStaff",
        "This employee is already a member of this service's staff.");

    public static readonly Error NotAStaffMember = Error.Validation(
        "Employees.NotAStaffMember",
        "This employee is not a member of this service's staff.");
}
