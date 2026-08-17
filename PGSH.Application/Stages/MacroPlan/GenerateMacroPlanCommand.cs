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
    int PeriodsPublished,
    /// <summary>
    /// Group×stage pairs the plan refused because the group's CNPN does not require that stage of
    /// its level. Surfaced rather than dropped: a plan that quietly leaves out a partition looks
    /// like it worked.
    /// </summary>
    int CohortsNotRequiredByCnpn = 0,
    /// <summary>
    /// Cells the arranger declined because the group was already placed in an overlapping period of
    /// another stage — the plan's partitions were authored to collide.
    /// </summary>
    int GroupConflicts = 0,
    /// <summary>
    /// Student assignments publication left alone because they already held a service period — an
    /// imported historical rotation, a délocalisation, a revalidation. Reported so a plan that
    /// publishes far fewer periods than expected explains itself.
    /// </summary>
    int SkippedAlreadyServed = 0);
