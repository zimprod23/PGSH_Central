using PGSH.Domain.Registrations;

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
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int StageId { get; set; }
    public Stage Stage { get; set; } = default!;
    public int AcademicGroupId { get; set; }
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
