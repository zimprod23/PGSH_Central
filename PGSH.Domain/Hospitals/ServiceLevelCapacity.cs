using PGSH.Domain.Common.Utils;

namespace PGSH.Domain.Hospitals;

/// <summary>
/// How many students of one <see cref="Level"/> a service will take at once.
///
/// A <see cref="Level"/> is already (programme × année) — "1ère année Médecine" is one row — so this
/// single key expresses everything a service's intake rules need: a quota per promotion, and, by
/// omission, which promotions it will not take at all. A service that only receives pharmaciens
/// simply has no row for any Médecine level.
///
/// ⚠ <b>A service with no rows admits everyone</b>, capped only by <see cref="Service.Capacity"/>.
/// That is not a placeholder for "unconfigured" — it is the honest reading of a service nobody has
/// restricted, and it is what keeps the ~100 imported services plannable without a data-entry pass.
/// Restriction is therefore an act, never a default: the first row a service gets closes it to every
/// level that has none.
///
/// ⚠ <b>Quotas replace <see cref="Service.Capacity"/>, they do not sit under it.</b> Once a service
/// has any quota, that number is no longer consulted for it: each promotion is measured against its
/// own quota and nothing else, so a service of 20 granting 10 and 15 will hold 25 without complaint.
/// The quotas <i>are</i> the statement of what the service accepts; a second ceiling silently
/// contradicting them was judged more confusing than the arithmetic. <see cref="Service.Capacity"/>
/// governs only services nobody has restricted — see <see cref="Service.CapacityFor"/>.
/// </summary>
public sealed class ServiceLevelCapacity
{
    public int Id { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; } = default!;

    public int LevelId { get; set; }
    public Level Level { get; set; } = default!;

    public int Capacity { get; set; }
}
