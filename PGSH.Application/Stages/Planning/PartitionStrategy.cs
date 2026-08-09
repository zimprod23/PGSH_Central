namespace PGSH.Application.Stages.Planning;

/// <summary>
/// How a promotion's groups are divided between rotation partitions. Both strategies produce
/// partitions of the same size, and the arranger cannot tell them apart — they differ only in *which*
/// groups end up together, and therefore in how the published répartition reads.
/// </summary>
public enum PartitionStrategy
{
    /// <summary>
    /// Fill the smallest partition each time, walking groups in number order. With two partitions that
    /// alternates on every group: <c>A = 1,3,5,7…</c> and <c>B = 2,4,6,8…</c> — in general each
    /// partition steps by the partition count.
    ///
    /// <para>This is what PGSH has always done, and it is the default so no existing plan moves. It is
    /// not a deliberate interleave: balance was the only property sought, and the stripe falls out of
    /// filling the smallest bucket one group at a time.</para>
    ///
    /// <para>⚠ Consequence on the printed matrix: a cell holds groups that are never consecutive, so
    /// <c>GroupNumberRanges</c> has nothing to collapse and prints
    /// <c>1, 3, 5, 7, 9, 11, 13, 15, 17, 19</c> where the faculty's own table reads <c>1-10</c>.</para>
    /// </summary>
    Interleaved,

    /// <summary>
    /// Cut the promotion into contiguous blocks in group-number order: <c>A = 1-40</c>,
    /// <c>B = 41-80</c>. This is the faculty's own convention in the published répartition, and the
    /// only strategy whose cells collapse to ranges.
    ///
    /// <para>⚠ Group numbers are not arbitrary. <c>AutoArrangeGroupsCommandHandler</c> buckets by
    /// (year, level, <c>CnpnVersionId</c>) and numbers sequentially per bucket, so each text's groups
    /// occupy a contiguous run. From 2026-2027, when one level holds two texts, a contiguous partition
    /// can therefore land entirely inside one text — where <see cref="Interleaved"/> would have mixed
    /// them. Since <c>CohortProvisioner</c> skips stages a group's text does not require, that changes
    /// which rows exist in each partition's half of the matrix. Intended behaviour either way, but
    /// choose it knowingly.</para>
    /// </summary>
    Contiguous,
}
