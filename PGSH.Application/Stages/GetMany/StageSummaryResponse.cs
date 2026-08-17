using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.GetMany;

// RotationMode is carried here as well as on the detail response: the list row is what the admin
// scans to see how each stage runs, and a summary that omits a field the edit form writes back is
// how HospitalSummaryResponse silently erased every hospital description.
public sealed record StageSummaryResponse(
    int Id,
    string Name,
    int Coefficient,
    int DurationInDays,
    string LevelLabel,
    StageRotationMode RotationMode);
