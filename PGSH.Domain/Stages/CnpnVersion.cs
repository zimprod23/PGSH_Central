using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// One issue of the Cahier des Normes Pédagogiques Nationales — a ministerial text governing a whole
/// programme: how many years it lasts and, through its <see cref="Curriculum"/> entries, what each
/// level requires.
///
/// <para><b>Why this exists.</b> A CNPN does not apply to an academic year — it applies to a
/// <i>cohort</i>, and it follows that cohort until they graduate. Arrêté 1650.25 (BO 7422,
/// 17 July 2025) took the Médecine doctorate from seven years to six with effect from 2024-2025, but
/// article 2 leaves every student registered before that year under the previous text. From
/// 2026-2027 onward a single (level, year) cell therefore holds students of both texts — the ones
/// arriving on schedule under the new CNPN, and the ones repeating under the old. 2,635 students in
/// the imported history have repeated a level, so this is routine, not hypothetical. Keying the
/// requirement set on (level, academic year) cannot express it; keying it on (version, level) can.
/// </para>
///
/// <para><b>Assignment is by entry, and sticky.</b> <see cref="AppliesToEntrantsFromAcademicYearId"/>
/// is matched against the student's <i>first</i> registration, never against the current year, and
/// the resulting stamp does not move afterwards however long the student takes. See
/// <c>CnpnAssignment</c> in the application layer.</para>
///
/// <para><b>Not every version assigns anyone.</b> A text can be superseded without ever having
/// governed an intake — arrêté 2175.22 (2022) amended 2174.18 and was then explicitly disapplied by
/// 1650.25, which sends pre-2024-2025 students back to 2174.18 in its <i>pre-amendment</i> form.
/// Leave <see cref="AppliesToEntrantsFromAcademicYearId"/> null for such a version: it is recorded
/// for history and never selected for a new entrant.</para>
/// </summary>
/// <remarks>
/// <para><b>This is the aggregate root of the text.</b> A <see cref="CnpnLevelEffectivity"/> has no
/// life of its own — it is one sentence of this text about one level — so it is declared and
/// withdrawn through <see cref="DeclareEffectivity"/> and <see cref="WithdrawEffectivity"/>, never
/// added to the collection by hand. That is what puts the four rules the text can decide alone
/// (a level of another programme, the withdrawal marker, a level beyond its span, a level already
/// spoken for) in one place instead of in whichever handler happened to need them.</para>
///
/// <para>⚠ <b>Three rules deliberately stay with the handler</b>, because they are about the
/// <i>other</i> texts and no aggregate can see them: a code unique within a programme, an intake
/// year claimed by only one text, and a (level, year) another text already takes effect for. Same
/// division as <see cref="AcademicYear"/>, where non-overlap is the handler's and « does it end
/// before it starts » is the year's. For the same reason the handler reads the
/// <see cref="CnpnSpanFloor"/> from the store and hands it over: the text owns the rule, not the
/// count.</para>
///
/// <para>The properties carry <c>init</c> accessors over explicit backing fields rather than plain
/// setters: an object initialiser — which is how the seeder, the importer, the migration and the
/// tests build a text — still works, while nothing can change one <em>afterwards</em> except through
/// the methods below. <see cref="AcademicProgram"/> has no mutator at all, and that is the type
/// saying what the documentation used to: curricula and student stamps hang off this row, so moving
/// it to another filière would orphan every one of them.</para>
/// </remarks>
public sealed class CnpnVersion : Entity
{
    /// <summary>No programme runs longer than this. A ceiling, not a statement about any text.</summary>
    public const int MaxTotalYears = 10;

    private string _code = default!;
    private string _label = default!;
    private int _totalYears;
    private string? _reference;
    private int? _appliesToEntrantsFromAcademicYearId;

    public int Id { get; set; }

    /// <summary>The arrêté number, as the text is cited — e.g. "1650.25", "2174.18".</summary>
    public string Code
    {
        get => _code;
        init => _code = value;
    }

    /// <summary>Human label for the pickers — e.g. "CNPN 2025 — Docteur en Médecine (6 ans)".</summary>
    public string Label
    {
        get => _label;
        init => _label = value;
    }

    /// <summary>
    /// The filière this text governs, fixed for the life of the row. A version belongs to exactly one
    /// programme, which is what makes a stamp carried across a réorientation meaningless — see
    /// <c>RegistrationCnpnStamper.Fallback</c>.
    /// </summary>
    public AcademicProgram AcademicProgram { get; init; }

    /// <summary>
    /// How many years the programme lasts under this text: 7 for arrêté 2174.18, 6 for 1650.25. This
    /// is what makes "a 6-year student has no 7th year" something the application can actually know.
    /// </summary>
    public int TotalYears
    {
        get => _totalYears;
        init => _totalYears = value;
    }

    /// <summary>Publication reference — Bulletin Officiel number and date.</summary>
    public string? Reference
    {
        get => _reference;
        init => _reference = value;
    }

    /// <summary>
    /// The first intake this text governs. A student is assigned the version with the greatest
    /// <see cref="AppliesToEntrantsFromAcademicYearId"/> at or before their entry year. Null means the
    /// version never governed an intake and is kept for history only.
    /// </summary>
    public int? AppliesToEntrantsFromAcademicYearId
    {
        get => _appliesToEntrantsFromAcademicYearId;
        init => _appliesToEntrantsFromAcademicYearId = value;
    }

    public AcademicYear? AppliesToEntrantsFromAcademicYear { get; set; }

    public ICollection<Curriculum> Curricula { get; set; } = new List<Curriculum>();

    /// <summary>
    /// The levels this text takes over from a given year onward, whoever is sitting in them — the
    /// second half of "who does this text bind", alongside
    /// <see cref="AppliesToEntrantsFromAcademicYearId"/>. Entry governs the new intake; these govern
    /// the promotions already in the building. See <see cref="CnpnLevelEffectivity"/>.
    /// </summary>
    public ICollection<CnpnLevelEffectivity> LevelEffectivities { get; set; } =
        new List<CnpnLevelEffectivity>();

    /// <summary>
    /// Corrects a recorded text: how it is cited, how it is labelled, how long the degree runs and
    /// which intake it begins to govern.
    /// </summary>
    /// <param name="floor">
    /// How far down the span is already spoken for, read from the store by the caller. ⚠ Deliberately
    /// <b>not</b> counted from <see cref="Curricula"/> and <see cref="LevelEffectivities"/> — see
    /// <see cref="CnpnSpanFloor"/> for why an aggregate that counts its own un-Included children
    /// silently stops enforcing this.
    /// </param>
    public Result Correct(
        string code, string label, int totalYears, string? reference,
        int? appliesToEntrantsFromAcademicYearId, CnpnSpanFloor floor)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure(CnpnVersionErrors.CodeRequired);

        if (string.IsNullOrWhiteSpace(label))
            return Result.Failure(CnpnVersionErrors.LabelRequired);

        if (totalYears is < 1 or > MaxTotalYears)
            return Result.Failure(CnpnVersionErrors.TotalYearsOutOfRange(totalYears, MaxTotalYears));

        if (floor.DeepestRecordedLevelYear > totalYears)
            return Result.Failure(CnpnVersionErrors.CannotShortenBelowRecordedLevel(
                totalYears, floor.DeepestRecordedLevelYear));

        if (floor.DeepestGoverningLevelYear > totalYears)
            return Result.Failure(CnpnVersionErrors.CannotShortenBelowEffectiveLevel(
                totalYears, floor.DeepestGoverningLevelYear));

        _code = code.Trim();
        _label = label.Trim();
        _totalYears = totalYears;
        _reference = reference?.Trim();
        _appliesToEntrantsFromAcademicYearId = appliesToEntrantsFromAcademicYearId;

        return Result.Success();
    }

    /// <summary>
    /// « Ce texte régit tel niveau à partir de telle année » — records that this text takes over a
    /// level from a year onward, whoever is sitting in it.
    /// </summary>
    /// <remarks>
    /// The rule is read once, as each registration is created, and frozen onto it, so declaring one
    /// moves nobody who is already stamped. <c>ApplyCnpnEffectivityCommand</c> exists for the order
    /// that actually goes wrong — the réinscription ran in September, the faculty settled the cut in
    /// October.
    /// </remarks>
    public Result<CnpnLevelEffectivity> DeclareEffectivity(
        Level level, AcademicYear fromYear, string? note, DateTime recordedOn)
    {
        string levelLabel = level.Label ?? $"niveau {level.Id}";

        // « Retrait » is a withdrawal marker, not a year of study: no students to govern, no stages,
        // no cohorts. Same guard as the partition cut and auto-arrange.
        if (!level.IsPromotion)
            return Result.Failure<CnpnLevelEffectivity>(LevelErrors.NotAPromotion(levelLabel));

        if (level.AcademicProgram != AcademicProgram)
            return Result.Failure<CnpnLevelEffectivity>(CnpnVersionErrors.EffectivityProgramMismatch(
                Code, AcademicProgram, levelLabel, level.AcademicProgram));

        // A text that stops at six years cannot take effect for a seventh.
        if (level.Year > _totalYears)
            return Result.Failure<CnpnLevelEffectivity>(
                CnpnVersionErrors.CannotShortenBelowEffectiveLevel(_totalYears, level.Year));

        if (LevelEffectivities.Any(e => e.LevelId == level.Id))
            return Result.Failure<CnpnLevelEffectivity>(
                CnpnVersionErrors.EffectivityAlreadyDeclared(Code, levelLabel, fromYear.Label));

        // No pre-set Id: on an already-tracked version a store-generated key makes EF classify the
        // child Modified instead of Added. Same gotcha as Curriculum.AddStage.
        var effectivity = new CnpnLevelEffectivity
        {
            CnpnVersionId      = Id,
            CnpnVersion        = this,
            LevelId            = level.Id,
            Level              = level,
            FromAcademicYearId = fromYear.Id,
            FromAcademicYear   = fromYear,
            Note               = note?.Trim(),
            RecordedOn         = recordedOn,
        };

        LevelEffectivities.Add(effectivity);
        Raise(new CnpnEffectivityDeclaredDomainEvent(Id, Code, level.Id, levelLabel, fromYear.Id, fromYear.Label));

        return effectivity;
    }

    /// <summary>
    /// Removes a rule. ⚠ <b>Prospective only.</b> Registrations already stamped under it keep their
    /// text — that is the whole point of stamping them, and un-stamping them would move requirement
    /// sets under students who have been studying against them. What the removal changes is which
    /// text the <i>next</i> registration at that level resolves to.
    /// </summary>
    /// <returns>
    /// The rule that was withdrawn, so the caller can delete the row. Severing a required
    /// relationship already marks the orphan deleted, but a row disappearing as a side effect of a
    /// collection edit is not something a reader of the handler should have to know.
    /// </returns>
    public Result<CnpnLevelEffectivity> WithdrawEffectivity(int effectivityId)
    {
        var effectivity = LevelEffectivities.FirstOrDefault(e => e.Id == effectivityId);

        if (effectivity is null)
            return Result.Failure<CnpnLevelEffectivity>(
                CnpnVersionErrors.EffectivityNotDeclaredHere(Code, effectivityId));

        LevelEffectivities.Remove(effectivity);
        Raise(new CnpnEffectivityWithdrawnDomainEvent(
            Id, Code, effectivity.LevelId, effectivity.FromAcademicYearId));

        return effectivity;
    }
}
