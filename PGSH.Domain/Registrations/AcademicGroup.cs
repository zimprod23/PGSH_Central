using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Domain.Registrations;

public sealed class AcademicGroup
{
    public int Id { get; set; }
    public string Label { get; set; } = default!; // e.g., "G22 - Temara Cluster"
    public int GroupNumber { get; set; }
    public string? GeographicZone { get; set; }
    public string? RotationGroup  { get; set; } // Persistent partition label (A, B, C…) across all stages

    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = default!;

    // The 20 fixed students
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    // The "Buses" this group takes for various stages
    public ICollection<Cohort> Cohorts { get; set; } = new List<Cohort>();
}