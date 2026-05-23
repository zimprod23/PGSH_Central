using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.Chef;

public sealed record AssignChefCommand(int ServiceId, Guid EmployeeId) : ICommand;
public sealed record RemoveChefCommand(int ServiceId) : ICommand;
