namespace PGSH.Domain.Registrations;

public sealed class AcademicYear
{
    public int Id { get; set; }
    public string Label { get; set; } // e.g., "2025-2026"
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; } // To easily pull the active year

    // Navigation
    public ICollection<AcademicGroup> Groups { get; set; } = new List<AcademicGroup>();
}
