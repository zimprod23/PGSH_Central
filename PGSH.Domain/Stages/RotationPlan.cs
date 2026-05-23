using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class RotationPlan
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public Stage Stage { get; set; }
    public string? Label { get; set; }
    public ICollection<RotationPlanSlot> Slots { get; set; } = new List<RotationPlanSlot>();
    public ICollection<Cohort> Cohorts { get; set; } = new List<Cohort>();
}

public sealed class RotationPlanSlot
{
    public int Id { get; set; }
    public int RotationPlanId { get; set; }
    public RotationPlan RotationPlan { get; set; }
    public int ServiceId { get; set; }
    public Service Service { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public int SequenceOrder { get; set; }
}
