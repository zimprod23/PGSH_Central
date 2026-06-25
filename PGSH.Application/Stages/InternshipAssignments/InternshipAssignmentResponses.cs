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
    bool IsPaused = false);

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
    string? PauseReason = null);
