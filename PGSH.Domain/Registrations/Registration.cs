using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public sealed class Registration : Entity
{
    public Guid Id { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; }
    public string Status { get; set; }
    public int LevelId { get; set; }
    public Level Level { get; set; }
    public Student Student { get; set; }
    public int? AcademicGroupId { get; set; } // The Group assigned for THIS year
    public AcademicGroup? AcademicGroup { get; set; }
    public Guid StudentId { get; set; }
    public FailureReasons? failureReasons { get; set; }
    public DateTime? RegistrationDate { get; set; }
    public ICollection<InternshipAssignment> InternshipAssignments { get; set; } = new List<InternshipAssignment>();
}
