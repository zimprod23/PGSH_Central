using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Hospitals.Services.PromotionFit;

/// <param name="Scope">What was asked for, in words, so a printed page says what it covers.</param>
/// <param name="Notes">
/// What the numbers rest on, said only when it is true of this data. See the handler.
/// </param>
public sealed record PromotionFitResponse(
    int AcademicYearId,
    string AcademicYearLabel,
    string Scope,
    PromotionFitTotals Totals,
    IReadOnlyList<PromotionFitRow> Promotions,
    IReadOnlyList<string> Notes);

/// <param name="WorstShortfall">
/// The deepest deficit anywhere in scope, as a positive number of places. ⚠ Never a sum of the
/// deficits: they are not paid out of one purse, and a promotion short 14 places in one stage and 6
/// in another does not need 20 anywhere.
/// </param>
public sealed record PromotionFitTotals(
    int Promotions,
    int PromotionsThatFit,
    int PromotionsOverCapacity,
    int PromotionsUnplaceable,
    int PromotionsWithoutStages,
    int Students,
    int Stages,
    int StagesOverCapacity,
    int StagesUnplaceable,
    int WorstShortfall,
    string? WorstShortfallStage);

/// <summary>
/// ⚠ The states are ordered by how bad they are and they are <b>not</b> interchangeable:
/// <see cref="Unplaceable"/> is « the arranger will refuse » and <see cref="OverCapacity"/> is
/// « it will place them and the publish will need forcing ». One is a catalogue gap, the other a
/// faculty decision, and collapsing them into « problème » loses which act is owed.
/// </summary>
public enum PromotionFitState
{
    /// <summary>Nobody is registered — there is nothing to fit, and it is not a pass.</summary>
    NoStudents,

    /// <summary>The level has no stage in the catalogue, so it has no axis and nothing to plan.</summary>
    NoStages,

    /// <summary>At least one stage has no service the rotation may draw. The arrange refuses outright.</summary>
    Unplaceable,

    /// <summary>Every stage is placeable, at least one over its services' declared places.</summary>
    OverCapacity,

    Fits,
}

/// <param name="Timeline"><c>T = Σkₛ</c>, the columns a partition needs to visit every stage.</param>
/// <param name="ColumnDays">How long one column of that axis lasts — the gcd of the durations.</param>
/// <param name="HeldStudents">
/// Registrations planning must leave alone (a blocking signalement). ⚠ Excluded from
/// <paramref name="Students"/> and reported beside it, because a promotion whose students are mostly
/// frozen otherwise reads as a promotion that comfortably fits.
/// </param>
/// <param name="Shortfall">Places missing in the worst stage, 0 when nothing is short.</param>
public sealed record PromotionFitRow(
    int LevelId,
    string LevelLabel,
    AcademicProgram AcademicProgram,
    int Students,
    int HeldStudents,
    int Timeline,
    int ColumnDays,
    int TotalDurationDays,
    PromotionFitState State,
    int Shortfall,
    IReadOnlyList<PromotionFitStageRow> Stages);

/// <summary>The three refusals are the arranger's own, named so the panel and the refusal agree.</summary>
public enum StageFitState
{
    /// <summary>No service is authorised at all — <c>Schedule.NoAllowedServices</c>.</summary>
    NoAllowedServices,

    /// <summary>Services are authorised, none admits this promotion — <c>Stages.NoServicesAdmitLevel</c>.</summary>
    NoServiceAdmits,

    /// <summary>Every authorised service is held for named rosters — <c>Stages.AllServicesReserved</c>.</summary>
    AllServicesReserved,

    /// <summary>The stage carries no duration, so it sits on no axis and its share cannot be computed.</summary>
    NoDuration,

    OverCapacity,

    Fits,
}

/// <param name="Periods"><c>kₛ</c> — columns spent here, derived from the duration.</param>
/// <param name="StudentsAtOnce"><c>N·kₛ/T</c>, rounded up: the slice standing here simultaneously.</param>
/// <param name="Places">
/// The sum of what the usable services take for <em>this</em> promotion — a quota where the service
/// has one, its total capacity where it has none. Reserved, external and non-admitting services
/// contribute nothing, which is why they are counted separately beside it.
/// </param>
/// <param name="Margin">
/// <paramref name="Places"/> − <paramref name="StudentsAtOnce"/>. Negative is the shortfall.
/// </param>
public sealed record PromotionFitStageRow(
    int StageId,
    string StageName,
    int DurationInDays,
    int Periods,
    int StudentsAtOnce,
    int Places,
    int Margin,
    StageFitState State,
    int AllowedServices,
    int UsableServices,
    int ReservedServices,
    int NotAdmittingServices,
    int ExternalServices,
    IReadOnlyList<PromotionFitServiceRow> Services);

/// <param name="AlsoAuthorisedBy">
/// The other promotions whose stages authorise this same service. ⚠ The panel's arithmetic does not
/// subtract them — nothing here knows when each promotion passes through — so this is the column
/// that says a comfortable margin may be spent twice.
/// </param>
public sealed record PromotionFitServiceRow(
    int ServiceId,
    string ServiceName,
    string HospitalName,
    int Places,
    bool ParticipatesInRotation,
    bool Admits,
    bool IsExternal,
    bool AllowsOverCapacity,
    IReadOnlyList<string> AlsoAuthorisedBy);
