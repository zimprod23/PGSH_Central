
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed class StageGroup:Entity
{
    public int Id { get; set; }
    public string Label { get; set; }
    public string? Description { get; set; }
    public ICollection<InternshipAssignment> internshipAssignments { get; set; } = new List<InternshipAssignment>();
    public int StageId { get; set; }
    public Stage Stage { get; set; }
}

