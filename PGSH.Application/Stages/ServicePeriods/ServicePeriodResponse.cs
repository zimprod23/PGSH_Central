namespace PGSH.Application.Stages.ServicePeriods;

public sealed record ServicePeriodResponse(
    Guid Id,
    Guid InternshipAssignmentId,
    string StudentFullName,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsComplete,
    bool HasEvaluation);
