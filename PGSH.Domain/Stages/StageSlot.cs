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
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ICollection<CohortSlotAssignment> Assignments { get; set; } = new List<CohortSlotAssignment>();
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
