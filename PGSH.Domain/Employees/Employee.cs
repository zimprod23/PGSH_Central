using PGSH.Domain.Users;


namespace PGSH.Domain.Employees;
public sealed class Employee : User
{
    public string? PPR { get; set; }
    public DateOnly? PvSignatureDate { get; set; }
    public Grade Grade { get; set; }
    public Position? Position { get; set; }
    public string? Label { get; set; }
    public WorkPlace? WorkPlace { get; set; }
}

public enum Grade
{
    MC,
    PES,
    PH,
    Nurse,
    Administrator
}
public enum Position
{
    ServiceChef,
    Normal
}

public enum EmployeeStatus
{
    Active,
    Inactive,
    Retired,
    OnLeave
}

public enum WorkPlace
{
    Hospital,
    Fmpr
}