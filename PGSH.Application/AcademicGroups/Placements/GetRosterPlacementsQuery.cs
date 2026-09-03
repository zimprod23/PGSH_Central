using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Placements;

/// <summary>
/// Which rosters of one promotion already go where a placement request needs them to go.
///
/// <para><b>Why this read exists.</b> A request like « Sbai fait tous ses stages à l'hôpital
/// militaire » or « ces deux étudiantes ensemble, stage A en S1 et stage B en S2 » has three possible
/// answers, and they are not equally good. The cheapest by far is « un groupe y va déjà » — the
/// student is then one <c>TransferStudentCommand</c> away, no cell is pinned, no roster is invented
/// and the arranger's balance is untouched. Until now that answer was <b>unreachable</b>: nothing
/// could be asked « quel groupe est au HMIMV ? », so the only way to find out was to read the
/// planning grid of every stage by eye, and the practical route was therefore to cut a dedicated
/// roster of one or two students.</para>
///
/// <para>⚠ <b>And a roster of two is not free.</b> <c>RotationArranger.BuildServiceQueue</c> weights
/// each service by how many <i>whole average-sized</i> cohorts it can hold, and a cohort is atomic —
/// so a two-student cohort occupies a queue position sized for an average roster (7 in 6ᵉ année) and
/// spends a full cohort's worth of that service's intake on two people. Nothing refuses it and
/// nothing reports it; the promotion's balance is simply a little wrong. That is the cost this read
/// exists to let a user avoid.</para>
/// </summary>
/// <param name="LevelId">
/// The promotion, with <paramref name="AcademicYearId"/>. Required: rosters are keyed
/// (year, level, number) and a number without its promotion identifies nothing.
/// </param>
/// <param name="StageId">Narrows the placement question to one stage — « qui fait <i>ce</i> stage en S1 ? ».</param>
/// <param name="ServiceId">The service the roster must be placed in. Mutually exclusive with <paramref name="HospitalId"/>.</param>
/// <param name="HospitalId">The hospital the roster must be placed in. Mutually exclusive with <paramref name="ServiceId"/>.</param>
public sealed record GetRosterPlacementsQuery(
    int LevelId,
    int? AcademicYearId = null,
    int? StageId = null,
    int? ServiceId = null,
    int? HospitalId = null,
    PlacementMatch Match = PlacementMatch.Anywhere,
    int PageNumber = 1,
    int PageSize = GetRosterPlacementsQuery.DefaultPageSize) : IQuery<RosterPlacementsResponse>
{
    /// <summary>
    /// Rosters per page. A promotion runs to ~200 of them (5ᵉ année 2026-2027 holds 192), and each
    /// carries its stages and the services under them, so the page has to be small enough that the
    /// nested lists stay a screenful.
    /// </summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// ⚠ A non-positive page size means « non précisé », never « une ligne ».
    /// <c>ToPaginatedResponseAsync</c> clamps a 0 <em>upward</em> to 1, so <c>?pageSize=0</c> would
    /// answer a promotion of 192 rosters with one and nothing anywhere saying so.
    /// </summary>
    public int EffectivePageNumber => PageNumber > 0 ? PageNumber : 1;

    public int EffectivePageSize => PageSize > 0 ? PageSize : DefaultPageSize;

    /// <summary>Whether a placement target was named at all — <c>Matches</c> means nothing without one.</summary>
    public bool HasTarget => ServiceId is not null || HospitalId is not null;
}

/// <summary>
/// How much of a roster's rotation the named service or hospital has to account for.
/// </summary>
public enum PlacementMatch
{
    /// <summary>
    /// At least one créneau is there. The answer to « qui passe par S1 ? » — and to the pair request,
    /// where each stage is asked about separately.
    /// </summary>
    Anywhere = 0,

    /// <summary>
    /// <b>Every</b> créneau in scope is there, and there is at least one. The military case: « tout
    /// au militaire » is not « il y va aussi ».
    /// <para>⚠ The « at least one » half is the whole guard — see
    /// <see cref="RosterHospitalPlacement.Unplaced"/>. Without it a roster nobody has arranged
    /// satisfies this vacuously and is returned as the strongest match in the promotion.</para>
    /// </summary>
    Exclusively,
}

public sealed record RosterPlacementsResponse(
    int AcademicYearId,
    int LevelId,
    PaginatedResponse<RosterPlacementResponse> Rosters,
    RosterPlacementSummary Summary);

/// <summary>
/// What is true of the whole promotion, whichever page is on screen — and, above all, what an empty
/// result means.
/// </summary>
/// <remarks>
/// ⚠ <b>« Aucun groupe » has two causes calling for opposite acts</b>, and only
/// <see cref="PlacedRosters"/> separates them: nobody goes to that hospital (choose another, or pin a
/// cell), or <i>nothing has been arranged at all</i> (go and arrange the promotion first). Collapsed
/// into a single zero the user reads the second as the first and starts solving the wrong problem.
/// This is not an edge case: measured 2026-09-03 the live base holds <b>0 planning cells on every
/// year</b>, so « rien n'est encore réparti » is the answer this read gives today. Same shape as
/// <c>RepartitionSummary.DeclaredSlotCount</c> and <c>ExportNotes</c>.
/// </remarks>
/// <param name="PromotionRosters">Rosters of this (année, niveau). « Non réparti » carries no level and is never one.</param>
/// <param name="PlacedRosters">Of those, how many hold at least one planning cell.</param>
/// <param name="MatchedRosters">How many satisfy the placement target — the same number as the page's total count.</param>
/// <param name="PromotionStages">Distinct stages the promotion's rosters hold a cohorte for.</param>
public sealed record RosterPlacementSummary(
    int PromotionRosters,
    int PlacedRosters,
    int MatchedRosters,
    int PromotionStages);

/// <param name="StageCount">Stages this roster holds a cohorte for.</param>
/// <param name="PlacedStageCount">Of those, how many have at least one cell.</param>
/// <param name="MatchedStageCount">Of those, how many satisfy the target. <c>0</c> when none was named.</param>
/// <param name="HospitalPlacement">
/// Null when the caller named no hospital — « la question n'a pas été posée » is not the same fact as
/// <see cref="RosterHospitalPlacement.Elsewhere"/>, and a default enum value would assert the second.
/// </param>
public sealed record RosterPlacementResponse(
    int GroupId,
    string Label,
    int GroupNumber,
    string? RotationGroup,
    int StudentCount,
    int StageCount,
    int PlacedStageCount,
    int MatchedStageCount,
    RosterHospitalPlacement? HospitalPlacement,
    IReadOnlyList<RosterStagePlacementResponse> Stages);

/// <param name="Matches">
/// Null when no target was named. A bool would make « pas de critère » indistinguishable from
/// « ne correspond pas ».
/// </param>
/// <param name="Services">
/// Empty when the roster holds a cohorte for the stage but no cell yet — which is a real and useful
/// answer (« ce stage reste à répartir pour ce groupe »), not a gap in the read.
/// </param>
public sealed record RosterStagePlacementResponse(
    int StageId,
    string StageName,
    bool? Matches,
    IReadOnlyList<RosterServicePlacementResponse> Services);

/// <summary>
/// One service a roster stands in for one stage, with the créneaux it holds there.
/// </summary>
/// <remarks>
/// Grouping the cells by service <i>is</i> the fold: a <c>SingleService</c> run of three columns is
/// one entry carrying <c>[4, 5, 6]</c> rather than three rows. <see cref="PeriodNumbers"/> stays a
/// list of numbers rather than a « P4-P6 » string — this is a screen, not a document, and a third
/// range formatter beside <c>GroupNumberRanges</c> and <c>CoveredSlotFolder</c> would be drift.
/// </remarks>
public sealed record RosterServicePlacementResponse(
    int ServiceId,
    string ServiceName,
    int HospitalId,
    string HospitalName,
    IReadOnlyList<int> PeriodNumbers);
