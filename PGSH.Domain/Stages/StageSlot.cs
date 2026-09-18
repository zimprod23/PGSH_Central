using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;
using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

/// <summary>
/// One numbered rotation window (P1, P2…) of a stage, for one academic year.
///
/// The year is part of the identity, not decoration: the window carries concrete dates, and the same
/// stage runs again next year over different ones. Keyed on (Stage, PeriodNumber) alone, defining
/// P1 for 2025-2026 would surface — with 2025 dates — for every other year the stage ever ran.
/// </summary>
public sealed class StageSlot
{
    /// <summary>EF's constructor. Not for callers — use <see cref="For"/>.</summary>
    private StageSlot() { }

    /// <summary>
    /// The only way to make a créneau: its identity is demanded, not remembered.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>C'est la garde qui manquait, et elle manquait dans le seul endroit où elle compte.</b>
    /// L'identité <c>(StageId, AcademicYearId, PeriodNumber)</c> est écrite dans <c>CLAUDE.md</c>,
    /// tenue par un index unique dans PostgreSQL, et n'était **garantie nulle part dans le code** :
    /// trois endroits construisaient un <c>StageSlot</c> par initialiseur d'objet, deux stampaient
    /// l'année, et le troisième l'oubliait — écrivant un créneau d'année <c>0</c>. Un initialiseur
    /// d'objet ne peut pas exiger un champ ; un constructeur, si.
    ///
    /// <para>Les trois clés sont en <c>private set</c> pour la même raison : changer l'année d'un
    /// créneau existant n'est pas une correction, c'est un autre créneau. Les dates et le libellé
    /// restent ouverts — c'est ce que « déplacer une colonne » modifie légitimement.</para>
    /// </remarks>
    public static Result<StageSlot> For(
        int stageId, int academicYearId, int periodNumber,
        DateOnly startDate, DateOnly endDate, string? label = null)
    {
        if (stageId <= 0)
            return Result.Failure<StageSlot>(StageErrors.SlotNeedsStage);

        // ⚠ Le défaut réel : sans année un créneau vaut pour toutes les promotions à la fois, et la
        // valeur par défaut d'un `int` — 0 — est une année qui n'existe pas.
        if (academicYearId <= 0)
            return Result.Failure<StageSlot>(StageErrors.SlotNeedsAcademicYear);

        if (periodNumber <= 0)
            return Result.Failure<StageSlot>(StageErrors.SlotNeedsPeriodNumber);

        if (endDate < startDate)
            return Result.Failure<StageSlot>(StageErrors.PeriodWindowReversed(startDate, endDate));

        return new StageSlot
        {
            StageId        = stageId,
            AcademicYearId = academicYearId,
            PeriodNumber   = periodNumber,
            StartDate      = startDate,
            EndDate        = endDate,
            Label          = label,
        };
    }

    public int Id { get; set; }
    public int StageId { get; private set; }
    public Stage Stage { get; set; } = default!;
    public int AcademicYearId { get; private set; }
    public AcademicYear AcademicYear { get; set; } = default!;
    public int PeriodNumber { get; private set; }
    public string? Label { get; set; }

    /// <summary>
    /// ⚠ <c>private set</c> : une colonne ne change pas de dates par affectation, mais par
    /// <see cref="MoveTo"/> ou <see cref="RelayTo"/> — et le choix entre les deux <b>est</b>
    /// l'information que <see cref="Source"/> porte. Sous une propriété ouverte, écrire la date en
    /// oubliant le marqueur serait la voie la plus courte, et un recalcul effacerait ensuite la
    /// décision d'un humain sans que rien ne l'ait signalé.
    /// </summary>
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>
    /// Qui a décidé de ces dates. ⚠ <see cref="SlotSource.Laid"/> par défaut, donc toute colonne
    /// écrite avant l'existence de cette colonne garde le sens qu'elle avait.
    /// </summary>
    public SlotSource Source { get; private set; } = SlotSource.Laid;

    /// <summary>
    /// Un recalcul d'axe laisse cette colonne où elle est et reprend sa cascade après elle.
    /// </summary>
    public bool IsMovedByHand => Source == SlotSource.MovedByHand;

    public ICollection<CohortSlotAssignment> Assignments { get; set; } = new List<CohortSlotAssignment>();

    /// <summary>
    /// Un humain déplace la colonne — et cela la <b>marque</b>, indissociablement.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Le marquage n'est pas un paramètre.</b> « Déplacer à la main » et « marquer comme
    /// déplacée à la main » sont un seul fait ; deux instructions séparées, c'est une occasion
    /// d'en écrire une sans l'autre — et la moitié qui manquerait est silencieuse, puisque la
    /// colonne aurait l'air normale jusqu'au recalcul qui l'écrase. Même raison que
    /// <see cref="For"/> : un invariant que l'appelant doit se rappeler n'en est pas un.
    ///
    /// <para>Les gardes d'ordre et de chevauchement ne sont <b>pas</b> ici : elles interrogent les
    /// autres colonnes et les périodes publiées, que cet objet ne voit pas. <c>SlotOverlapGuard</c>,
    /// <c>GroupScheduleConflictGuard</c> et <c>PublishedPeriodShifter</c> les portent, avant
    /// l'appel.</para>
    /// </remarks>
    public Result MoveTo(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return Result.Failure(StageErrors.PeriodWindowReversed(startDate, endDate));

        StartDate = startDate;
        EndDate   = endDate;
        Source    = SlotSource.MovedByHand;
        return Result.Success();
    }

    /// <summary>
    /// L'axe repose la colonne. Ne marque rien : c'est la machine qui écrit, et c'est l'état par
    /// défaut.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Elle refuse une colonne déplacée à la main plutôt que de la reposer.</b> Le refus vit
    /// ici, dans l'objet qui porte le marqueur, et non seulement dans le planificateur qui l'appelle :
    /// c'est la leçon de <c>InternshipAssignment.Reschedule</c>, dont l'invariant reposait
    /// entièrement sur la bonne volonté de <c>PublishedPeriodShifter.PlanAsync</c>. Le planificateur
    /// écarte ces colonnes <i>et</i> les compte ; ce refus-ci est le filet, et l'atteindre signale une
    /// incohérence, pas un cas d'usage.
    /// </remarks>
    public Result RelayTo(DateOnly startDate, DateOnly endDate)
    {
        if (IsMovedByHand)
            return Result.Failure(StageErrors.SlotMovedByHandCannotBeRelaid(PeriodNumber));

        if (endDate < startDate)
            return Result.Failure(StageErrors.PeriodWindowReversed(startDate, endDate));

        StartDate = startDate;
        EndDate   = endDate;
        return Result.Success();
    }
}

/// <summary>
/// One cell of the planning grid: this cohort spends this period in this service. The narrowest of
/// the three things called "groupe" in conversation — see <see cref="Registrations.AcademicGroup"/>
/// for the distinction. It needs no year of its own: both the cohort and the slot carry one, and the
/// unique index on (cohort, slot) keeps them consistent.
/// </summary>
public sealed class CohortSlotAssignment
{
    public int Id { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; } = default!;
    public int StageSlotId { get; set; }
    public StageSlot StageSlot { get; set; } = default!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;

    /// <summary>
    /// Whether the rotation wrote this cell or a human chose it. ⚠ <c>Arranged</c> by default, so
    /// every cell written before this column existed keeps the meaning it had.
    /// </summary>
    public CellSource Source { get; set; } = CellSource.Arranged;

    /// <summary>
    /// A pinned cell is a human's decision, and the arranger treats it exactly as it treats a
    /// published one — never deleted, never rewritten, excluded from the column being balanced.
    /// </summary>
    public bool IsPinned => Source == CellSource.Pinned;
}
