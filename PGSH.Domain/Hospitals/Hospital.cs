using PGSH.Domain.Common.Utils;

namespace PGSH.Domain.Hospitals;

public class Hospital
{
    public int Id { get; set; }
    public int CenterId { get; set; }
    public Center Center { get; set; }
    public HospitalType HospitalType { get; set; } = HospitalType.None;
    public string Name { get; set; }
    public string City { get; set; }
    public string? Description { get; set; } = "";
    public Localization? LocalisationMaps { get; set; }
    public string? Email { get; set; }
    public ICollection<Service> services { get; set; } = new List<Service>();

}
