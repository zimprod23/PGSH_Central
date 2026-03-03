using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Services.Create;

public record CreateServiceCommand(
    int HospitalId,
    string Name,
    ServiceType ServiceType,
    int Capacity,
    string Description) : ICommand<int>;