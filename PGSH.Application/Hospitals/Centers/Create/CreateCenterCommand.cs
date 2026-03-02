using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Create;

public record CreateCenterCommand(
    string Name,
    CenterType CenterType,
    string? City,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ) : ICommand<int>;