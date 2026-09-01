using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Export;

/// <summary>
/// One stage attempt, flat. ⚠ Flat is the requirement, not a preference: a collection folded inside
/// a <c>Select</c> is the shape Npgsql refuses, so the périodes are fetched by their own top-level
/// query and joined in memory on <see cref="AssignmentId"/>.
/// </summary>
internal sealed record StageAssignmentExportRow(
    Guid AssignmentId,
    string LastName,
    string FirstName,
    string Cne,
    string Appogee,
    string YearLabel,
    AcademicProgram Program,
    int RegistrationLevelYear,
    string? RegistrationLevelLabel,
    AcademicProgram StageProgram,
    int StageLevelYear,
    string? StageLevelLabel,
    string? GroupLabel,
    int? GroupNumber,
    string? RotationGroup,
    int StageId,
    string StageName,
    int Coefficient,
    StageRotationMode RotationMode,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result);

/// <summary>
/// One période, flat, with its évaluation's raw parts rather than its mark. The mark is
/// <see cref="StageScoring"/>'s to compute — it weights the objective scores — so the objectives come
/// back from their own query and the mark is asked for in memory, never restated here.
/// </summary>
internal sealed record StagePeriodExportRow(
    Guid PeriodId,
    Guid AssignmentId,
    int ServiceId,
    string ServiceName,
    string? HospitalName,
    DateOnly Start,
    DateOnly End,
    bool IsStarted,
    bool IsComplete,
    bool IsInterrupted,
    bool IsPaused,
    bool IsDelocalized,
    bool FromGrid,
    Guid? EvaluationId,
    EvaluationMode? EvaluationMode,
    decimal? TotalScore,
    EvaluationOutcome? Outcome);

/// <summary>One objective score, keyed by its évaluation. Folded in memory into the mark.</summary>
internal sealed record ObjectiveScoreExportRow(
    Guid EvaluationId,
    int? Score,
    int Weight);

/// <summary>
/// One planning créneau a période was materialised from, with the créneau's <em>own</em> window —
/// which is precisely what a folded <c>SingleService</c> run stops the période's own dates from
/// saying. Keyed on the période, joined in memory.
/// </summary>
internal sealed record PeriodSlotExportRow(
    Guid PeriodId,
    int PeriodNumber,
    string? Label,
    DateOnly Start,
    DateOnly End);
