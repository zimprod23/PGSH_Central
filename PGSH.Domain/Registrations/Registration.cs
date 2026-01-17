using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed class Registration : Entity
{
    public Guid Id { get; set; }
    public DateOnly AcademicYear { get; set; }
    public FailureReasons? failureReasons { get; set; }
    public string Status { get; set; }
    public Level Level { get; set; }
    public Student Student { get; set; }
    public Guid StudentId { get; set; }
    public ICollection<InternshipAssignment> InternshipAssignments { get; set; } = new List<InternshipAssignment>();
}
