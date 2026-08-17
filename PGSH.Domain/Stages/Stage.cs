using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

public sealed class Stage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Coefficient { get; set; } = 1;
    public string? Description { get; set; }
    public int DurationInDays { get; set; } = 30;

    /// <summary>
    /// Whether the group changes service between this stage's périodes or serves them all in one.
    /// See <see cref="StageRotationMode"/> — the axis is identical either way, only the service
    /// assignment and the number of evaluations differ.
    /// </summary>
    public StageRotationMode RotationMode { get; set; } = StageRotationMode.PerPeriod;

    public int LevelId { get; set; }
    public Level Level { get; set; } = default!;
    public ICollection<StageObjective> Objectives { get; set; } = new List<StageObjective>();
    public ICollection<StageSlot> Slots { get; set; } = new List<StageSlot>();
    public ICollection<Service> AllowedServices { get; set; } = new List<Service>();
}
