using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

public sealed class Cohort
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int StageId { get; set; }
    public Stage Stage { get; set; }
    public int AcademicGroupId { get; set; }
    public AcademicGroup AcademicGroup { get; set; }
    public int? RotationPlanId { get; set; }
    public RotationPlan? RotationPlan { get; set; }
    public ICollection<InternshipAssignment> Assignments { get; set; } = new List<InternshipAssignment>();
}

public sealed class CohortMembership
{
    public Guid Id { get; set; }
    public Guid InternshipAssignmentId { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? TransferReason { get; set; }
}
