using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.Staff;

public sealed record AssignStaffCommand(int ServiceId, Guid EmployeeId) : ICommand;
public sealed record RemoveStaffCommand(int ServiceId, Guid EmployeeId) : ICommand;
