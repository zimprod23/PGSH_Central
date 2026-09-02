using PGSH.Application.Stages.Attendance;
using PGSH.Application.Stages.Evaluations;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.InternshipAssignments.GetRecord;

/// <summary>
/// The full record of one student's attempt at a stage: every period he served, the mark and verdict
/// each earned, his attendance, and the complete evaluation detail — plus the rolled-up stage note and
/// result. Backs the "click a student in the notes list → see everything" detail view and is the basis
/// for the fiche de validation.
/// </summary>
public sealed record StudentStageRecordResponse(
    Guid AssignmentId,
    Guid RegistrationId,
    string StudentFullName,
    string StudentAppogee,
    string? StudentCne,
    int StageId,
    string StageName,
    string? LevelLabel,
    int CohortId,
    string CohortLabel,
    string? GroupLabel,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    bool AllPeriodsEvaluated,
    IReadOnlyList<StagePeriodRecord> Periods);

public sealed record StagePeriodRecord(
    Guid PeriodId,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsStarted,
    bool IsComplete,
    bool IsInterrupted,
    bool IsDelocalized,
    decimal? Mark,
    bool? Validated,
    ServiceEvaluationResponse? Evaluation,
    int PresentCount,
    int AbsentCount,
    int JustifiedAbsentCount,
    int LateCount,
    IReadOnlyList<AttendanceResponse> Attendance);
