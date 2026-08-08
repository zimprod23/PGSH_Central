using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;

namespace PGSH.Application.Students.GetParcours;

/// <summary>
/// A student's whole internship path, year by year — every registration they hold and every stage
/// attempt served under it.
///
/// The student portal used to read its stages from <c>GET /internship-assignments?registrationId=…</c>
/// with the <em>current</em> registration, so a 6th-year student saw six years of work reduced to the
/// months since September, and last year's marks vanished the day a new registration was created.
/// A parcours is per student, not per registration: only folding the registrations together answers
/// "what have I done, and how did it go?".
/// </summary>
/// <remarks>
/// Deliberately unpaginated, and safe to be: the response is bounded by registrations held (one per
/// year enrolled, single digits even for a repeating student) × stages of a level (also single
/// digits). Unlike a group's student list, it cannot grow with the size of the faculty.
/// </remarks>
public sealed record StudentParcoursResponse(
    Guid StudentId,
    string StudentFullName,
    ParcoursTotals Totals,
    IReadOnlyList<ParcoursYear> Years);

/// <summary>
/// Attempt counts, split along the axis the dashboard used to conflate:
/// <see cref="InternshipAssignment.Status"/> is workflow progress,
/// <see cref="InternshipAssignment.Result"/> is the academic outcome. A stage whose rotations are
/// finished has left <see cref="Planned"/> — it is <see cref="AwaitingVerdict"/> until the marks are
/// all in, then <see cref="Validated"/> or <see cref="Failed"/>. The five buckets are disjoint and
/// cover every attempt.
/// </summary>
public sealed record ParcoursTotals(
    int Planned,
    int Ongoing,
    int AwaitingVerdict,
    int Validated,
    int Failed)
{
    public int Total => Planned + Ongoing + AwaitingVerdict + Validated + Failed;
}

public sealed record ParcoursYear(
    Guid RegistrationId,
    int AcademicYearId,
    string AcademicYearLabel,
    int LevelId,
    string? LevelLabel,
    int LevelYear,
    RegistrationStatus RegistrationStatus,
    int? AcademicGroupId,
    string? AcademicGroupLabel,
    // This registration sits in the academic year flagged IsCurrent — the authoritative "now",
    // rather than whichever registration happens to sort first.
    bool IsCurrent,
    ParcoursTotals Totals,
    IReadOnlyList<ParcoursStage> Stages);

public sealed record ParcoursStage(
    Guid AssignmentId,
    int StageId,
    string StageName,
    int Coefficient,
    // The level the STAGE belongs to, which is not always the level of the registration carrying it:
    // a retake of an earlier level's stage hangs off the registration the student holds now. Showing
    // the registration's level there would label a 1st-year stage as 6th-year work.
    int StageLevelId,
    string? StageLevelLabel,
    // 1 for a first sitting, 2 for the first retake, and so on — ordered by academic year.
    int AttemptNumber,
    int CohortId,
    string CohortLabel,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    // Span of the actual rotations; null while nothing has been scheduled yet.
    DateOnly? StartDate,
    DateOnly? EndDate,
    int PeriodsTotal,
    int PeriodsComplete,
    // Every non-interrupted rotation has been evaluated, so FinalScore is the stage's final note
    // and not a running partial mean.
    bool AllPeriodsEvaluated);
