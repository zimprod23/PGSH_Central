using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.InternshipAssignments.GetDossier;

/// <summary>
/// A student's standing at one level across <em>every</em> registration they hold there.
///
/// A repeating student carries a fresh <see cref="Registration"/> each year, and a fresh
/// <see cref="InternshipAssignment"/> per stage under it — so the per-assignment record answers
/// "how did this attempt go?" but never "what does this student still owe at this level?".
/// That second question decides what gets planned for the coming year and what has to be
/// re-opened in revalidation, and it can only be answered by folding the registrations together.
/// </summary>
public sealed record StudentLevelDossierResponse(
    Guid StudentId,
    string StudentFullName,
    string? StudentCne,
    string? StudentAppogee,
    int LevelId,
    string? LevelLabel,
    int StagesTotal,
    int StagesValidated,
    int StagesOutstanding,
    bool IsLevelComplete,
    IReadOnlyList<DossierRegistration> Registrations,
    IReadOnlyList<DossierStage> Stages);

public sealed record DossierRegistration(
    Guid RegistrationId,
    int AcademicYearId,
    string AcademicYearLabel,
    RegistrationStatus Status,
    int? AcademicGroupId,
    string? AcademicGroupLabel,
    int AttemptCount);

public sealed record DossierStage(
    int StageId,
    string StageName,
    int Coefficient,
    DossierStageState State,
    decimal? BestScore,
    int AttemptCount,
    IReadOnlyList<DossierAttempt> Attempts);

public sealed record DossierAttempt(
    Guid AssignmentId,
    Guid RegistrationId,
    int AcademicYearId,
    string AcademicYearLabel,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,

    /// <summary>The verdict pronounced on the year this attempt was served in.</summary>
    RegistrationStatus YearOutcome,

    /// <summary>
    /// True when the year was failed, so this attempt establishes nothing — the student repeats it
    /// from scratch, stages included. It stays in the list because it happened, and the screen says
    /// so: « validé — année redoublée » is a different thing from « validé », and reading a row that
    /// counts and one that does not as the same badge is what made the two indistinguishable.
    /// </summary>
    bool AnnulledByFailedYear);

/// <summary>
/// What a student still owes on one stage of the level. Derived from the attempts' stored
/// <see cref="StageAssignmentResult"/> — the verdict the domain already computed through
/// <see cref="StageScoring"/> — never recomputed here.
/// </summary>
public enum DossierStageState
{
    /// <summary>No attempt on any registration: ordinary planning, not a retake.</summary>
    NotAttempted,

    /// <summary>An attempt exists that has not reached a verdict yet; nothing to decide.</summary>
    InProgress,

    /// <summary>
    /// Passed on some registration whose year still stands. A stage once acquired is never repeated
    /// — but a pass inside a year the faculty failed is not an acquisition, because the redoublant
    /// serves that year again in full.
    /// </summary>
    Validated,

    /// <summary>Every attempt came back <c>NonValidé</c> — this is what revalidation re-opens.</summary>
    ToRevalidate,
}
