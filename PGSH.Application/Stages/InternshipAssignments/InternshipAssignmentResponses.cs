using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.InternshipAssignments;

public sealed record InternshipAssignmentSummaryResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    int StageId,
    string StageName,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    // True while any of the assignment's active periods is suspended (e.g. an exam week). The
    // assignment Status itself stays Ongoing — this surfaces the per-period pause on the row.
    bool IsPaused = false,
    // True once every (non-interrupted) period has an evaluation — only then is FinalScore/Result a
    // final stage verdict worth showing in the notes list.
    bool AllPeriodsEvaluated = false);

public sealed record InternshipAssignmentResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    IReadOnlyList<ServicePeriodSummary> ServicePeriods);

public sealed record ServicePeriodSummary(
    Guid Id,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsComplete,
    bool HasEvaluation,
    bool IsStarted = false,
    bool IsPaused = false,
    string? PauseReason = null,
    // The whole stage was served outside the faculty: this is an ad-hoc, pre-completed period
    // evaluated from the paper fiche de validation. No in-app chef supervises it.
    bool IsDelocalized = false,
    string? DelocalizationReason = null);
