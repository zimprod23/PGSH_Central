using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Centers.Delete;

public record DeleteCenterCommand(int Id) : ICommand;