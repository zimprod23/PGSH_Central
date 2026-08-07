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
    public int Capacity { get; set; } = 20;
    public int HospitalId { get; set; }
    public Hospital Hospital { get; set; }

    public ICollection<Employee> Staff { get; set; } = new List<Employee>();

    public Guid? ServiceChefId { get; private set; }
    public Employee? ServiceChef { get; private set; }

    // Append-only trail of past and present chef tenures — see ServiceChefAssignment.
    public ICollection<ServiceChefAssignment> ChefHistory { get; set; } = new List<ServiceChefAssignment>();

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

