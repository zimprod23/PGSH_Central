using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

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
}
