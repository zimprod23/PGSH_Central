using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// The order a stage's authorised services are walked in when a rotation is arranged.
///
/// <para>Pure — no store, no clock — like <c>PeriodAxis</c>, <c>RotationTiling</c> and
/// <c>StagePeriodFolder</c>, and for the same reason: the arithmetic that decides which run of
/// group numbers lands in which service is exact, so its cases should be exact too rather than
/// approximately seeded.</para>
///
/// <para>⚠ <b>The order is the list; the rank is only how it is stored.</b> Every operation returns
/// the whole sequence rather than patching one position, so a rank is always the place a service
/// actually holds — contiguous from 1. Holes would make the number shown beside a service disagree
/// with the place it takes in the queue: one number meaning two things.</para>
/// </summary>
public static class ServiceRotationOrder
{
    /// <summary>
    /// Where a row with no authored rank sorts: last, never first.
    ///
    /// <para>⚠ The column defaults to 0, so a join row inserted by anything that does not go through
    /// these commands — a data migration, a corrective script — would otherwise sort <b>ahead</b> of
    /// every service somebody deliberately placed, and hand it the first run of group numbers. The
    /// arrange then falls back to id order for it, which is exactly the pre-Rank behaviour.</para>
    /// </summary>
    public static int SortKeyOf(int rank) => rank > 0 ? rank : int.MaxValue;

    /// <summary>The stored rank of each service in an ordered list — its position, 1-based.</summary>
    public static IReadOnlyDictionary<int, int> RanksFor(IReadOnlyList<int> serviceIdsInOrder) =>
        serviceIdsInOrder
            .Select((serviceId, index) => (serviceId, rank: index + 1))
            .ToDictionary(x => x.serviceId, x => x.rank);

    /// <summary>
    /// <paramref name="requestedOrder"/>, accepted only if it is exactly a permutation of
    /// <paramref name="currentServiceIds"/>.
    ///
    /// <para>⚠ <b>A partial list is refused rather than completed.</b> Silently appending whatever
    /// the caller left out states an order nobody authored, in the one place whose whole purpose is
    /// that the order is authored — and the likeliest cause of a short list is a page opened before
    /// somebody else added a service, not an intention to leave it last.</para>
    /// </summary>
    public static Result<IReadOnlyList<int>> Reorder(
        IReadOnlyCollection<int> currentServiceIds,
        IReadOnlyList<int> requestedOrder)
    {
        var duplicated = requestedOrder
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        var current = currentServiceIds.ToHashSet();
        var requested = requestedOrder.ToHashSet();

        var unknown = requestedOrder.Where(id => !current.Contains(id)).Distinct().ToList();
        var missing = currentServiceIds.Where(id => !requested.Contains(id)).ToList();

        if (duplicated.Count > 0 || unknown.Count > 0 || missing.Count > 0)
            return Result.Failure<IReadOnlyList<int>>(
                StageErrors.ServiceOrderNotAPermutation(missing.Count, unknown.Count, duplicated.Count));

        // Not the implicit operator: an empty list is a legitimate order for a stage that authorises
        // nothing, and Result<T>'s conversion would only trip over null anyway.
        return Result.Success<IReadOnlyList<int>>([.. requestedOrder]);
    }

    /// <summary>
    /// The order after a newly authorised service joins it — last, because a service added to the
    /// list has no claim on a position somebody chose for the ones already there.
    /// </summary>
    public static IReadOnlyList<int> Append(IReadOnlyList<int> currentInOrder, int addedServiceId) =>
        currentInOrder.Contains(addedServiceId) ? currentInOrder : [.. currentInOrder, addedServiceId];

    /// <summary>
    /// The order after a service leaves it — the survivors keep their relative places, with no hole
    /// left where it was.
    /// </summary>
    public static IReadOnlyList<int> Without(IReadOnlyList<int> currentInOrder, int removedServiceId) =>
        [.. currentInOrder.Where(id => id != removedServiceId)];
}
