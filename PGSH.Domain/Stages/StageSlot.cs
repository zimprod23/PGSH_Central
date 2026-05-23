using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class StageSlot
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public Stage Stage { get; set; } = default!;
    public int PeriodNumber { get; set; }
    public string? Label { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ICollection<CohortSlotAssignment> Assignments { get; set; } = new List<CohortSlotAssignment>();
}

public sealed class CohortSlotAssignment
{
    public int Id { get; set; }
    public int CohortId { get; set; }
    public Cohort Cohort { get; set; } = default!;
    public int StageSlotId { get; set; }
    public StageSlot StageSlot { get; set; } = default!;
    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;
}
