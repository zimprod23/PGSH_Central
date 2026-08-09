using PGSH.Domain.Common.Utils;
using PGSH.Domain.Employees;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Domain.Hospitals;

public sealed class Service
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string? Specialty { get; set; }
    public ServiceType ServiceType { get; set; }

    /// <summary>
    /// The physical ceiling: how many students of any kind the service holds at once. Per-level
    /// intake rules live in <see cref="LevelCapacities"/> and sit <i>underneath</i> this number,
    /// never replacing it — see <see cref="ServiceLevelCapacity"/>.
    /// </summary>
    public int Capacity { get; set; } = 20;

    public int HospitalId { get; set; }
    public Hospital Hospital { get; set; }

    /// <summary>
    /// Where the service actually is, when that differs from its hospital's own coordinates — a
    /// pavilion on the far side of the grounds is a different journey for a student. Null means
    /// "wherever the hospital is", which is the honest answer for most services.
    /// </summary>
    public Localization? LocalisationMaps { get; set; }

    public ICollection<ServiceLevelCapacity> LevelCapacities { get; set; } = new List<ServiceLevelCapacity>();

    public ICollection<Employee> Staff { get; set; } = new List<Employee>();

    public Guid? ServiceChefId { get; private set; }
    public Employee? ServiceChef { get; private set; }

    // Append-only trail of past and present chef tenures — see ServiceChefAssignment.
    public ICollection<ServiceChefAssignment> ChefHistory { get; set; } = new List<ServiceChefAssignment>();

    /// <summary>True once anyone has authored an intake rule — before that the service takes all comers.</summary>
    public bool HasLevelRestrictions => LevelCapacities.Count > 0;

    /// <summary>Whether students of <paramref name="levelId"/> may be placed here at all.</summary>
    public bool Admits(int levelId) =>
        !HasLevelRestrictions || LevelCapacities.Any(c => c.LevelId == levelId);

    /// <summary>
    /// The <b>one</b> limit that governs students of <paramref name="levelId"/> here — quotas do not
    /// sit under <see cref="Capacity"/>, they replace it:
    ///
    /// <list type="bullet">
    /// <item>no quotas authored → <see cref="Capacity"/>, counted across every promotion at once;</item>
    /// <item>quotas authored → this level's quota, counted against that level alone, and
    /// <see cref="Capacity"/> is not consulted;</item>
    /// <item>quotas authored but none for this level → 0. The service does not take them.</item>
    /// </list>
    ///
    /// ⚠ So a restricted service's <see cref="Capacity"/> is <b>dead data</b>: a service of 20
    /// granting 10 and 15 will hold 25, and nothing objects. That is deliberate — the quotas are the
    /// statement of what the service accepts, and a second ceiling contradicting them was judged
    /// more confusing than the arithmetic. Any UI showing both must say which one is in force.
    /// </summary>
    public int CapacityFor(int levelId)
    {
        if (!HasLevelRestrictions)
            return Capacity;

        return LevelCapacities.FirstOrDefault(c => c.LevelId == levelId)?.Capacity ?? 0;
    }

    public void SetLevelCapacity(int levelId, int capacity)
    {
        var existing = LevelCapacities.FirstOrDefault(c => c.LevelId == levelId);
        if (existing is not null)
        {
            existing.Capacity = capacity;
            return;
        }

        // No Id assigned: on an already-tracked Service, a pre-set store-generated key makes EF
        // classify the child Modified instead of Added. See InternshipAssignment.Delocalize.
        LevelCapacities.Add(new ServiceLevelCapacity { ServiceId = Id, LevelId = levelId, Capacity = capacity });
    }

    public void RemoveLevelCapacity(int levelId)
    {
        var existing = LevelCapacities.FirstOrDefault(c => c.LevelId == levelId);
        if (existing is not null) LevelCapacities.Remove(existing);
    }

    /// <summary>
    /// Makes the intake rules exactly <paramref name="quotas"/>. An empty set reopens the service to
    /// every level, which is the only way back from a restriction entered by mistake.
    /// </summary>
    public void ReplaceLevelCapacities(IReadOnlyCollection<(int LevelId, int Capacity)> quotas)
    {
        foreach (var levelId in LevelCapacities.Select(c => c.LevelId).Except(quotas.Select(q => q.LevelId)).ToList())
            RemoveLevelCapacity(levelId);

        foreach (var (levelId, capacity) in quotas)
            SetLevelCapacity(levelId, capacity);
    }

    public void AddStaff(Employee employee)
    {
        if (!Staff.Any(e => e.Id == employee.Id))
            Staff.Add(employee);
    }

    public void RemoveStaff(Employee employee)
    {
        var member = Staff.FirstOrDefault(e => e.Id == employee.Id);
        if (member is null) return;
        Staff.Remove(member);
        if (ServiceChefId == employee.Id)
            RemoveChef();
    }

    public Result AssignChef(Employee employee)
    {
        if (!Staff.Any(e => e.Id == employee.Id))
            return Result.Failure(EmployeeErrors.NotInStaff);
        if (employee.Position != Position.ServiceChef)
            return Result.Failure(EmployeeErrors.WrongPosition);
        if (ServiceChefId == employee.Id)
            return Result.Success();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        CloseOpenChefTenure(today);
        ChefHistory.Add(new ServiceChefAssignment
        {
            ServiceId  = Id,
            EmployeeId = employee.Id,
            StartDate  = today,
        });

        ServiceChef = employee;
        ServiceChefId = employee.Id;
        return Result.Success();
    }

    public void RemoveChef()
    {
        CloseOpenChefTenure(DateOnly.FromDateTime(DateTime.UtcNow));
        ServiceChef = null;
        ServiceChefId = null;
    }

    private void CloseOpenChefTenure(DateOnly date)
    {
        var open = ChefHistory.FirstOrDefault(h => h.EndDate is null);
        if (open is not null) open.EndDate = date;
    }
}

public enum ServiceType
{
    Biologie,
    Chirurgie,
    Medical
}

