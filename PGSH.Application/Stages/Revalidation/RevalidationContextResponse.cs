using PGSH.Domain.Registrations;

namespace PGSH.Application.Stages.Revalidation;

/// <summary>
/// Everything the operator needs before re-opening a stage, and — the reason this exists — the
/// duration <b>the student's own text</b> states for it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>The catalogue is the wrong number here, and measurably so.</b> MED3 Chirurgie reads 30
/// jours ouvrables in the catalogue since it was aligned on arrêté 1650.25, while the 92 students
/// still owing it in 6ᵉ année are governed by 2174.18, which states <b>66</b>. The one such window on
/// record ran 65 j.o. So a proposal taken from <c>Stage.DurationInDays</c> would be wrong for exactly
/// the population that reaches this screen — every revalidation is by definition a student on an
/// older text.</para>
/// <para>Hence <see cref="ProposedWindow"/> is laid from
/// <see cref="RevalidationText.DurationInDays"/> — the requirement set of
/// <c>r.CnpnVersionId ?? r.Student.CnpnVersionId</c> — and is <b>null when no text states one</b>.
/// A proposal invented from the catalogue would be indistinguishable from one somebody authored.</para>
/// </remarks>
public sealed record RevalidationContextResponse(
    Guid RegistrationId,
    int StageId,
    string StageName,
    int StageLevelId,
    string? StageLevelLabel,

    /// <summary>Whether the command would accept this today, decided by the same rules it uses.</summary>
    bool CanOpen,
    string? RefusalCode,
    string? RefusalMessage,

    /// <summary>The text governing this registration. Null means never resolved, not "owes nothing".</summary>
    RevalidationText? GoverningText,

    /// <summary>What the timeless catalogue says — shown beside the text's figure, never instead of it.</summary>
    int CatalogueDurationInDays,
    int CatalogueCoefficient,

    RevalidationPriorAttempt? LastFailure,
    RevalidationWindow? ProposedWindow,
    IReadOnlyList<RevalidationCohortOption> Cohorts,

    /// <summary>
    /// The cohorte the command would fall back to if the caller names none — the one for this stage
    /// on the roster this registration already sits in. Null means naming one is <b>required</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ Without this the dialog cannot tell « laisse-le vide » from « ceci ne peut que échouer ».
    /// A 6ᵉ année student revalidating a 3ᵉ année stage normally has no such cohorte — his roster
    /// runs 6ᵉ année stages — and the button offered the act anyway, which
    /// <c>NoGroupForRevalidation</c> then refused. Read through the same query the command falls
    /// back on, so the two cannot disagree.
    /// </remarks>
    int? FallbackCohortId);

/// <param name="FromRegistration">
/// True when the stamp was read off the registration, false when it fell back to the student's own.
/// The distinction is the whole reason <c>Registration.CnpnVersionId</c> exists: what a student owed
/// at one level in one year must not move when his current text does.
/// </param>
/// <param name="StatesThisStage">
/// False when the text has no requirement recorded for this (level, stage) — which is ordinary, since
/// 1650.25's sets are not fully entered. Absence is not zero, so nothing is proposed.
/// </param>
public sealed record RevalidationText(
    int CnpnVersionId,
    string Code,
    string Label,
    RegistrationCnpnSource? Source,
    bool FromRegistration,
    bool StatesThisStage,
    int? DurationInDays,
    int? Coefficient);

public sealed record RevalidationPriorAttempt(
    Guid RegistrationId,
    int AcademicYearId,
    string AcademicYearLabel,
    int? ServiceId,
    string? ServiceName,
    DateOnly? StartDate,
    DateOnly? EndDate,

    /// <summary>
    /// What he actually served, in worked days — the figure to compare the text's against. It is the
    /// only evidence on this screen that is neither a catalogue value nor a text value.
    /// </summary>
    int? WorkingDaysServed);

public sealed record RevalidationWindow(
    DateOnly Start,
    DateOnly End,
    int WorkingDays,
    int CalendarDays,

    /// <summary>A lunar date inside the window is still an estimate, so the window may move.</summary>
    bool HasProvisionalDates,
    IReadOnlyList<string> HolidaysHit);

public sealed record RevalidationCohortOption(
    int CohortId,
    int AcademicGroupId,
    string? GroupLabel,
    int GroupNumber,
    string? RotationGroup);
