using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Domain.HospitalServices;

public sealed class Service : Entity
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public Nature Nature { get; set; }
    public Speciality Speciality { get; set; }
    public Localization Localization { get; set; }
    public int Capacity { get; set; } = 20;
}
