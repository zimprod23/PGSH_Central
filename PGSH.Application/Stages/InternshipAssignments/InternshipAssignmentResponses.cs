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
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result);

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
    bool HasEvaluation);
