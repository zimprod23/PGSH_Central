using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Update;

public record UpdateCenterCommand(
    int Id,
    string Name,
    CenterType CenterType,
    string? City,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ) : ICommand;