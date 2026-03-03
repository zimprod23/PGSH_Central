using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Services.Delete;

public record DeleteServiceCommand(int Id) : ICommand;