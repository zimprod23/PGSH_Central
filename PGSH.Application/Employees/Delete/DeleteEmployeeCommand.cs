using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Employees.Delete;

public sealed record DeleteEmployeeCommand(Guid Id) : ICommand;
