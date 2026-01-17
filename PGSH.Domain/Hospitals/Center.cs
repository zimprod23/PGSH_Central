using PGSH.Domain.Common.Utils;

namespace PGSH.Domain.Hospitals;

public class Center
{
    public int Id { get; set; }
    public string Name { get; set; }
    public CenterType CenterType { get; set; } = CenterType.None;
    public string? City { get; set; }
    public Localization? LocalisationMaps { get; set; }
    public ICollection<Hospital> Hospitals { get; private set; } = [];
}