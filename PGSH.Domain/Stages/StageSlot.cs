using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;

namespace PGSH.Domain.Stages;

/// <summary>
/// One numbered rotation window (P1, P2…) of a stage, for one academic year.
///
/// The year is part of the identity, not decoration: the window carries concrete dates, and the same
/// stage runs again next year over different ones. Keyed on (Stage, PeriodNumber) alone, defining
/// P1 for 2025-2026 would surface — with 2025 dates — for every other year the stage ever ran.
/// </summary>
public sealed class StageSlot
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public Stage Stage { get; set; } = default!;
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = default!;
    public int PeriodNumber { get; set; }
    public string? Label { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ICollection<CohortSlotAssignment> Assignments { get; set; } = new List<CohortSlotAssignment>();
}

/// <summary>
/// One cell of the planning grid: this cohort spends this period in this service. The narrowest of
/// the three things called "groupe" in conversation — see <see cref="Registrations.AcademicGroup"/>
/// for the distinction. It needs no year of its own: both the cohort and the slot carry one, and the
/// unique index on (cohort, slot) keeps them consistent.
/// </summary>
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
