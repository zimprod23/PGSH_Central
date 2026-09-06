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
    /// How many students the service holds at once, counted across every promotion — <b>but only
    /// while no <see cref="LevelCapacities"/> row exists</b>. The moment one does, this number stops
    /// being consulted: quotas <i>replace</i> it rather than sitting under it, so a service of 20
    /// granting 10 and 15 holds 25 and nothing objects. Read the limit through
    /// <see cref="CapacityFor"/>, never off this property.
    /// </summary>
    /// <remarks>
    /// ⚠ This comment used to say quotas sit "underneath this number, never replacing it", which is
    /// the opposite of what <see cref="CapacityFor"/> does and of the rule the publish guard
    /// enforces. Corrected 2026-08-14. Note also that all 148 imported services carry the default
    /// 20 and none has a quota, so every capacity decision in the base today is measured against a
    /// number nobody authored.
    /// </remarks>
    public int Capacity { get; set; } = 20;

    /// <summary>
    /// Whether a publication may be forced past this service's number — « autoriser le dépassement
    /// d'effectif ». <b>True by default</b>: the ceiling is a target the faculty routinely exceeds
    /// (measured 2026-08-14, 233 of 353 planned cells are over it), and a service that has never
    /// said otherwise has not refused anything.
    /// </summary>
    /// <remarks>
    /// <para>Set to false, the service's number stops being negotiable: the checkbox no longer lifts
    /// it, and the refusal says so. It is the chef's own statement about his service — some will not
    /// take a group over the number whatever the planning needs — and it is the <b>only</b> way to
    /// make an occupancy limit binding, since the override is ticked as a matter of routine on a base
    /// this over-subscribed.</para>
    ///
    /// <para>⚠ <b>It governs the number, never admissibility.</b> <see cref="Admits"/> is already
    /// unforceable whatever this says; this flag moves the <i>occupancy</i> half from the waivable
    /// side to the hard one. So a service may refuse over-capacity and still take every promotion,
    /// and the two rules are read separately — see <c>SchedulePublisher.EnsureIntakeAsync</c>.</para>
    ///
    /// <para>⚠ It binds <b>publication</b>, not planning. <c>RotationArranger</c> still balances by
    /// <see cref="CapacityFor"/> and will happily fill a firm service past its number; what changes
    /// is that the plan can no longer be published without being corrected first. That is the right
    /// order — a plan is a draft, and refusing to draw one would leave the admin nowhere to see the
    /// problem.</para>
    /// </remarks>
    public bool AllowsOverCapacity { get; set; } = true;

    /// <summary>
    /// A place the faculty does not supervise — a CHU in another region, a private clinic, a
    /// hospital abroad — held in the catalogue only so that a délocalisation has something to name.
    /// <b>False for every service of the faculty's own network</b>, and the flag is what separates
    /// the two kinds of row that would otherwise be indistinguishable.
    /// </summary>
    /// <remarks>
    /// <para>An external service is <b>not a rotation candidate</b>: it cannot be added to a stage's
    /// allowed services, and <c>RotationArranger</c> drops it from the pool even if an older row put
    /// it there. Nobody is ever placed here by a plan — the only way in is
    /// <c>InternshipAssignment.Delocalize</c>, which is a statement that the student went somewhere
    /// we do not run.</para>
    ///
    /// <para>⚠ <b>It has no capacity, and that is the point.</b> <see cref="Capacity"/> and
    /// <see cref="LevelCapacities"/> are not consulted for it, and its cells never enter
    /// <c>ServiceOccupancyCalculator</c>. A number we invented for a hospital we do not run is not a
    /// ceiling, and letting it into the saturation maths would put a fictional load beside the real
    /// ones in the very report the délocalisation exists to relieve.</para>
    ///
    /// <para>⚠ <b>It has no chef and never will</b>, so no worklist covers it and no evaluation
    /// arrives through the app: the verdict comes back on paper and scolarité records it. The stage
    /// export writes « hors faculté » in its chef column for such a rotation rather than leaving it
    /// blank — a blank reads as a name the export failed to resolve, and sends somebody looking for
    /// it.</para>
    /// </remarks>
    public bool IsExternal { get; set; }

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

