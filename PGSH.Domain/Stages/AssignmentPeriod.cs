using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class AssignmentPeriod
{
    public Guid Id { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public Guid AssignementId { get; set; }
    public InternshipAssignment InternshipAssignment { get; set; }
    public int ServiceId { get; set; }
    public Service Service { get; set; }

}
