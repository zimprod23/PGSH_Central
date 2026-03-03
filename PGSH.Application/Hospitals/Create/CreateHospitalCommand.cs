using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Create;

public record CreateHospitalCommand(
    int CenterId,
    string Name,
    HospitalType HospitalType,
    string City,
    string? Description,
    string? Email,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ) : ICommand<int>;