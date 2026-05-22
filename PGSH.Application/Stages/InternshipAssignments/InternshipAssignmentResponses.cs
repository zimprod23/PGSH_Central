namespace PGSH.Application.Stages.InternshipAssignments;

public sealed record InternshipAssignmentSummaryResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    int StageId,
    string Status,
    decimal? FinalScore,
    string? Result);

public sealed record InternshipAssignmentResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    string Status,
    decimal? FinalScore,
    string? Result,
    IReadOnlyList<ServicePeriodSummary> ServicePeriods);

public sealed record ServicePeriodSummary(
    Guid Id,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsComplete,
    bool HasEvaluation);
