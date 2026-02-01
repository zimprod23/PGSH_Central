using PGSH.Domain.Common.Utils;

namespace PGSH.Domain.Stages;

public sealed class Stage
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Coefficient { get; set; } = 1;
    public string? Description { get; set; }
    public int DurationInDays { get; set; } = 30;
    public int LevelId { get; set; }
    public Level Level { get; set; }
    public ICollection<StageObjective> Objectives { get; set; } = new List<StageObjective>();
}
