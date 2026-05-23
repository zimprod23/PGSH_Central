using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Employees.GetById;

public sealed record GetEmployeeByIdQuery(Guid Id) : IQuery<EmployeeResponse>;
