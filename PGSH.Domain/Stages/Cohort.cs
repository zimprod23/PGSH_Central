using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// One roster doing one stage — (<see cref="AcademicGroup"/> × <see cref="Stage"/>). The unit a
/// rotation is planned against.
///
/// ⚠ It is <b>year-constituted through its group</b>, not through the stage: the stage is catalog
/// data that outlives every promotion, so a cohort exists per (group, year) and filtering on
/// <see cref="StageId"/> alone reaches all of them. Measured on the imported data, "CHIRURGIE" has
/// 563 cohorts across six years. Always pair it with
/// <c>AcademicGroup.AcademicYearId</c> — see the year rules in CLAUDE.md.
///
/// Not to be confused with the roster itself or with one cell of the grid; see
/// <see cref="AcademicGroup"/> for the three-way distinction.
/// </summary>
public sealed class Cohort
{
    /// <summary>EF's constructor. Not for callers — use <see cref="For(int, int, string)"/>.</summary>
    private Cohort() { }

    /// <summary>
    /// The only way to make a cohorte: both halves of its identity are demanded, not remembered.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>There is no unique index behind <c>(StageId, AcademicGroupId)</c>.</b> Unlike
    /// <see cref="StageSlot"/>, whose identity PostgreSQL enforces, nothing in the schema stops a
    /// second cohorte for the same pair from being written — <c>CreateCohortCommandHandler</c> and
    /// <c>CohortProvisioner</c> each check for the duplicate themselves, in code, and a path that
    /// forgets to is not caught by anything. This factory does not close that hole either: a
    /// constructor sees one object, never the table. What it closes is the other half — a cohorte
    /// built without a stage or without a roster, which is a row no read can interpret at all.</para>
    ///
    /// <para>The two keys are <c>private set</c> for the reason <c>StageSlot</c>'s are: re-pointing a
    /// cohorte at another stage or another roster is not a correction, it is a different cohorte, and
    /// every affectation and every cell hanging off it would silently follow. The label stays open —
    /// it is what a roster's rename legitimately changes.</para>
    /// </remarks>
    public static Result<Cohort> For(int stageId, int academicGroupId, string label)
    {
        if (stageId <= 0)
            return Result.Failure<Cohort>(StageErrors.CohortNeedsStage);

        if (academicGroupId <= 0)
            return Result.Failure<Cohort>(StageErrors.CohortNeedsRoster);

        return new Cohort { StageId = stageId, AcademicGroupId = academicGroupId, Label = label };
    }

    /// <summary>
    /// The same demand, for a graph whose parents have no key yet — <c>LegacyImportPlanner</c> builds
    /// the année, the roster and the cohorte in one pass and lets the store number all three.
    /// </summary>
    /// <remarks>
    /// ⚠ It exists so that path does not have to go round the factory: an overload taking ids would
    /// be satisfied by two zeros there, which is precisely the row this class refuses to make.
    /// </remarks>
    public static Result<Cohort> For(Stage stage, AcademicGroup group, string label)
    {
        if (stage is null)
            return Result.Failure<Cohort>(StageErrors.CohortNeedsStage);

        if (group is null)
            return Result.Failure<Cohort>(StageErrors.CohortNeedsRoster);

        return new Cohort { Stage = stage, AcademicGroup = group, Label = label };
    }

    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int StageId { get; private set; }
    public Stage Stage { get; set; } = default!;
    public int AcademicGroupId { get; private set; }
    public AcademicGroup AcademicGroup { get; set; } = default!;
    public ICollection<InternshipAssignment> Assignments { get; set; } = new List<InternshipAssignment>();
    public ICollection<CohortSlotAssignment> SlotAssignments { get; set; } = new List<CohortSlotAssignment>();
}

public sealed class CohortMembership
{
    public Guid Id { get; set; }
    public Guid InternshipAssignmentId { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? TransferReason { get; set; }

    // Definitive by default so the initial membership and permanent moves need no extra wiring;
    // only a temporary transfer flips this and records OriginalCohortId for the auto-revert.
    public TransferType TransferType { get; set; } = TransferType.Definitive;

    // Where a temporary transfer returns the student. Null for the initial membership and
    // definitive moves.
    public int? OriginalCohortId { get; set; }
}
