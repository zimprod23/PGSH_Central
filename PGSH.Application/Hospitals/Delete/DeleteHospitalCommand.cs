using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Hospitals.Delete;

public record DeleteHospitalCommand(int Id) : ICommand;