using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Domain.Registrations;

/// <summary>
/// A roster: the fixed set of students who move together through a year. Nothing more.
///
/// ⚠ "Groupe" in conversation usually means something narrower than this class. Three distinct
/// things share the word, and confusing them is how the year-scoping bugs got written:
///
/// <list type="bullet">
/// <item><b><see cref="AcademicGroup"/></b> — the roster, per (year, level). No stage, no service.</item>
/// <item><b><see cref="Cohort"/></b> — that roster <i>doing one stage</i>: (group × stage). This is
/// what a stage's "groups" are, and why a stage accumulates one per year it runs.</item>
/// <item><b><see cref="CohortSlotAssignment"/></b> — that cohort <i>in one period, in one service</i>:
/// (cohort × slot → service). When someone says "the group in Cardiologie in P2", this is the row.</item>
/// </list>
///
/// So a group is not "in a service" — it is in a <i>sequence</i> of them, one per period, and the
/// service lives two levels out. Reaching for <see cref="AcademicGroup"/> when you mean one cell of
/// the rotation grid is a category error the compiler cannot catch.
///
/// The year is constitutive, not decoration: a roster outside a year is not a roster, which is why
/// <see cref="AcademicYearId"/> is non-nullable.
/// </summary>
public sealed class AcademicGroup
{
    public int Id { get; set; }
    public string Label { get; set; } = default!; // e.g., "G22 - Temara Cluster"
    public int GroupNumber { get; set; }
    public string? GeographicZone { get; set; }
    public string? RotationGroup  { get; set; } // Persistent partition label (A, B, C…) across all stages

    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = default!;

    // Optional level association — set on manual creation, inferred from students for auto-arranged groups
    public int? LevelId { get; set; }
    public Level? Level { get; set; }

    // The 20 fixed students
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    // The "Buses" this group takes for various stages
    public ICollection<Cohort> Cohorts { get; set; } = new List<Cohort>();
}