
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed class StageGroup : Entity
{
    public int Id { get; set; }
    public string Label { get; set; }
    public string? Description { get; set; }

    public int StageId { get; set; }
    public Stage Stage { get; set; }

    // Shared schedule (rotations)
    public ICollection<StageGroupPeriod> Periods { get; set; } = new List<StageGroupPeriod>();

    // Students
    public ICollection<InternshipAssignment> InternshipAssignments { get; set; }
        = new List<InternshipAssignment>();
}
