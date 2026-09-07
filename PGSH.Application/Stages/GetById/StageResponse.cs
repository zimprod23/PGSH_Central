using PGSH.Application.Stages.Levels;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.GetById;

public sealed record StageResponse(
    int Id,
    string Name,
    int Coefficient,
    string? Description,
    int DurationInDays,
    StageRotationMode RotationMode,
    LevelResponse? LevelResponse,
    StageObjectiveResponse[] StageObjectiveResponse,
    AllowedServiceSummary[] AllowedServices
    );

/// <summary>
/// <paramref name="Rank"/> is the position the service takes in the rotation queue — a planning
/// fact, not a display preference: <c>RotationArranger</c> hands the first run of group numbers to
/// the service ranked first, in the first période.
///
/// <para>⚠ The list used to come back sorted by hospital then name, which is a fourth order —
/// neither the one authored, nor the one the arranger walked (<c>OrderBy(Service.Id)</c>, i.e.
/// legacy import order). So nothing on screen said which service was first, in the one place where
/// being first decides something.</para>
/// </summary>
/// <summary>
/// One authorised service of a stage, with the position the rotation walks it in and how it is
/// filled.
/// </summary>
/// <remarks>
/// ⚠ <c>PlacementMode</c> travels with the rank because they answer one question together. A
/// screen showing the order without the mode lists a service in the rotation queue that the
/// rotation will never choose — which reads as a service the arranger keeps skipping.
/// </remarks>
public sealed record AllowedServiceSummary(
    int Id,
    string Name,
    string HospitalName,
    int Rank = 0,
    ServicePlacementMode PlacementMode = ServicePlacementMode.Rotation);

public record StageObjectiveResponse(
    string Label,
    string? Description,
    int Weight,
    bool IsMandatory
    );


