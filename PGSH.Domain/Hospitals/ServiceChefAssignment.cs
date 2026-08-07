using PGSH.Domain.Employees;

namespace PGSH.Domain.Hospitals;

/// <summary>
/// Audit trail of who led a <see cref="Service"/> and when. A new record opens when a chef is
/// assigned and closes (<see cref="EndDate"/> set) when he is replaced or removed — so past
/// evaluations remain traceable to the chef in charge at the time, across academic years.
/// </summary>
public sealed class ServiceChefAssignment
{
    public Guid Id { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = default!;

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
