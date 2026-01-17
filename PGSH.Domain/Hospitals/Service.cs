using PGSH.Domain.Employees;
using PGSH.Domain.Stages;

namespace PGSH.Domain.Hospitals;

public sealed class Service
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public ServiceType ServiceType { get; set; }
    //public Nature Nature { get; set; }
    public int Capacity { get; set; } = 20;
    public int HospitalId { get; set; }
    public Hospital Hospital { get; set; }

    public ICollection<Employee> Staff = new List<Employee>();
    public ICollection<AssignmentPeriod> assignmentPeriods = new List<AssignmentPeriod>();
    //[Obsolete("Must be removed")]
    // Private backing field for the ServiceChef property
    public Guid? ServiceChefId { get; private set; }
    public Employee? ServiceChef
    {
        get;
        private set;
    }
    public void AddStaff(Employee employee)
    {
        if (!Staff.Contains(employee))
            Staff.Add(employee);
    }

    public void AssignChef(Employee employee)
    {
        if (!Staff.Contains(employee))
            throw new InvalidOperationException("The chef must be part of the service staff.");

        if (employee.Position != Position.ServiceChef)
            throw new InvalidOperationException("This employee cannot be assigned as ServiceChef.");

        ServiceChef = employee;
        ServiceChefId = employee.Id;
    }
}

public enum ServiceType
{
    Biologie,
    Chirurgie,
    Medical
}

