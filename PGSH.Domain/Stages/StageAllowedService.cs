using PGSH.Domain.Hospitals;

namespace PGSH.Domain.Stages;

/// <summary>
/// One authorised service of a stage, carrying the position it takes in the rotation.
///
/// <para>The join used to be a bare pair, so <c>RotationArranger</c> walked the list
/// <c>OrderBy(s =&gt; s.Id)</c> — the order the <see cref="Service"/> row was created in the
/// catalogue, i.e. the legacy import order. Nobody chose it, and it decides which contiguous run of
/// group numbers lands in which service: <c>BuildServiceQueue</c> emits each service's block
/// consecutively, and the earliest column takes phase 0, so the first groups go to whichever service
/// is first in the list.</para>
///
/// <para>⚠ <b>The rank is a planning input, not a display preference.</b> Reordering changes what the
/// next auto-arrange writes for the whole promotion. It never moves a cell already written — the
/// arrange is the act that reads it.</para>
/// </summary>
public sealed class StageAllowedService
{
    public int StageId { get; set; }
    public int ServiceId { get; set; }

    /// <summary>
    /// 1-based position in the rotation queue. Unique within a stage: two services sharing a rank
    /// would leave the order to whatever the provider returned, which is the state this column
    /// exists to remove.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Whether the rotation may place a cohort here, or the service is held for named rosters and
    /// filled only by a pinned cell. ⚠ <c>Rotation</c> by default: authorising a service must keep
    /// meaning what it meant before this column existed.
    /// </summary>
    public ServicePlacementMode PlacementMode { get; set; } = ServicePlacementMode.Rotation;

    /// <summary>
    /// Whether <c>RotationArranger</c> may draw this service from the pool. The rank still applies
    /// to a reserved service — it simply never comes up, since nothing but a pin puts a cohort
    /// there.
    /// </summary>
    public bool ParticipatesInRotation => PlacementMode == ServicePlacementMode.Rotation;

    public Service Service { get; set; } = default!;
}
