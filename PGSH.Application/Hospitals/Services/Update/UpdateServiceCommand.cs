using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Services.Update;

public record UpdateServiceCommand(
    int Id,
    string Name,
    string Description,
    ServiceType ServiceType,
    int Capacity,
    int HospitalId,
    string? Specialty,
    string? LocalizationX = null,
    string? LocalizationY = null,
    string? LocalizationZ = null,
    IReadOnlyCollection<ServiceLevelCapacityRequest>? LevelCapacities = null) : ICommand;
