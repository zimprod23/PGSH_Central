using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.MacroPlan;

/// <summary>
/// One-shot macro plan: for each (partition, stage, window) entry, optionally
/// create cohorts, affect students, auto-arrange services, and publish — fanning
/// out to the shared planning services. Stateless; driven by the UI matrix.
/// </summary>
public sealed record GenerateMacroPlanCommand(
    int AcademicYearId,
    IReadOnlyList<PartitionStagePlan> Plans,
    bool AssignStudents = true,
    bool AutoArrange = true,
    bool Publish = false,
    bool AllowOverCapacity = false) : ICommand<MacroPlanResult>;

public sealed record PartitionStagePlan(
    string RotationGroup,
    int StageId,
    IReadOnlyList<int> PeriodNumbers);

public sealed record MacroPlanResult(
    int CohortsCreated,
    int CohortsSkipped,
    int StudentsAssigned,
    int CellsArranged,
    int SaturatedServices,
    int CohortsPublished,
    int PeriodsPublished);
