using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class Cohort
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public int StageId { get; set; }
    public Stage Stage { get; set; }
    public ICollection<CohortRotationTemplate> RotationTemplates { get; set; } = new List<CohortRotationTemplate>();
}

public sealed class CohortRotationTemplate
{
    public int Id { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public int ServiceId { get; set; }
    public Service Service { get; set; }
    public int SequenceOrder { get; set; } // e.g., 1st rotation, 2nd rotation
    //public int DurationDays { get; set; }
}

public sealed class CohortMembership
{
    public Guid Id { get; set; }
    public Guid InternshipAssignmentId { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; } // Null means currently active in this cohort
    public string? TransferReason { get; set; }
}
