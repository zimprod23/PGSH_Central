namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Pure helper for the persistent partition labels (A, B, C…) carried by
/// <c>AcademicGroup.RotationGroup</c>. Existing labels are always preserved;
/// only groups without one are filled. Shared by partition assignment and schedule auto-arrange so the
/// labelling rule lives in exactly one place.
/// </summary>
internal static class PartitionAllocator
{
    public static string LabelFor(int index) =>
        ((char)('A' + (index % 26))).ToString() + (index >= 26 ? (index / 26).ToString() : "");

    /// <summary>
    /// Builds the ordered set of partition labels. When any items already carry a
    /// label the existing count wins (a previous run's structure is preserved);
    /// otherwise <paramref name="requestedCount"/> partitions are created. Any
    /// out-of-range label already in use is appended so it is never dropped.
    /// </summary>
    public static List<string> BuildLabels(IEnumerable<string?> existingLabels, int requestedCount)
    {
        var existing = existingLabels.OfType<string>().Distinct().OrderBy(l => l).ToList();

        int numPartitions = existing.Count > 0 ? existing.Count : Math.Max(1, requestedCount);

        var labels = Enumerable.Range(0, numPartitions).Select(LabelFor).ToList();

        foreach (var l in existing.Where(l => !labels.Contains(l)))
            labels.Add(l);

        return labels;
    }

    /// <summary>
    /// Assigns a label to each currently-unlabelled item, in the supplied order. Returns the new labels
    /// only for the items that lacked one — already-labelled items are left untouched, so a re-run never
    /// reshuffles a plan already built on the current partitioning. Use
    /// <see cref="ReassignAll{TId}"/> to deliberately re-cut a promotion.
    /// </summary>
    public static Dictionary<TId, string> AssignUnlabelled<TId>(
        IReadOnlyList<(TId Id, string? Label)> itemsInOrder,
        int requestedCount,
        PartitionStrategy strategy = PartitionStrategy.Interleaved)
        where TId : notnull
    {
        var labels = BuildLabels(itemsInOrder.Select(i => i.Label), requestedCount);
        var unlabelled = itemsInOrder.Where(i => i.Label is null).Select(i => i.Id).ToList();

        if (unlabelled.Count == 0)
            return [];

        // Existing members count toward the balance, so filling the gaps in a half-labelled promotion
        // tops up the smaller partitions rather than starting the count from zero.
        var counts = labels.ToDictionary(l => l, l => itemsInOrder.Count(i => i.Label == l));

        return strategy == PartitionStrategy.Contiguous
            ? Contiguous(unlabelled, labels, counts)
            : Interleaved(unlabelled, labels, counts);
    }

    /// <summary>
    /// Re-cuts <paramref name="itemsInOrder"/> into <paramref name="partitionCount"/> partitions,
    /// <b>ignoring and overwriting</b> whatever labels they carry. Returns a label for every item.
    /// </summary>
    /// <remarks>
    /// ⚠ This moves groups between partitions, which is exactly what any plan built on the previous
    /// partitioning encodes. The caller is responsible for refusing when that plan must not move — see
    /// <c>AssignRotationGroupsCommandHandler</c>, which will not re-cut across a published cell.
    /// </remarks>
    public static Dictionary<TId, string> ReassignAll<TId>(
        IReadOnlyList<TId> itemsInOrder,
        int partitionCount,
        PartitionStrategy strategy)
        where TId : notnull
    {
        var labels = Enumerable.Range(0, Math.Max(1, partitionCount)).Select(LabelFor).ToList();
        var counts = labels.ToDictionary(l => l, _ => 0);

        return strategy == PartitionStrategy.Contiguous
            ? Contiguous(itemsInOrder, labels, counts)
            : Interleaved(itemsInOrder, labels, counts);
    }

    /// <summary>
    /// Fill the smallest partition each time. The tie-break is the label order, so with equal counts
    /// the walk goes A, B, C, A, B, C… — which is what stripes a promotion by the partition count.
    /// </summary>
    private static Dictionary<TId, string> Interleaved<TId>(
        IReadOnlyList<TId> items, List<string> labels, Dictionary<string, int> counts)
        where TId : notnull
    {
        var assignments = new Dictionary<TId, string>();

        foreach (var id in items)
        {
            // Explicit tie-break on label order rather than relying on MinBy's first-minimum over
            // Dictionary enumeration, whose order is an implementation detail rather than a guarantee.
            string label = labels.MinBy(l => (counts[l], labels.IndexOf(l)))!;
            assignments[id] = label;
            counts[label]++;
        }

        return assignments;
    }

    /// <summary>
    /// Cut into contiguous blocks in the supplied order, largest blocks first so the remainder is
    /// spread over the leading partitions rather than dumped on the last one. Partitions that already
    /// hold members need fewer, which keeps a gap-fill from overshooting.
    /// </summary>
    private static Dictionary<TId, string> Contiguous<TId>(
        IReadOnlyList<TId> items, List<string> labels, Dictionary<string, int> counts)
        where TId : notnull
    {
        int total = items.Count + counts.Values.Sum();
        int baseSize = total / labels.Count;
        int remainder = total % labels.Count;

        var quota = labels
            .Select((label, i) => (label, want: baseSize + (i < remainder ? 1 : 0) - counts[label]))
            .Select(x => (x.label, want: Math.Max(0, x.want)))
            .ToList();

        var assignments = new Dictionary<TId, string>();
        int cursor = 0;

        foreach (var (label, want) in quota)
        {
            for (int taken = 0; taken < want && cursor < items.Count; taken++, cursor++)
                assignments[items[cursor]] = label;
        }

        // Rounding, or partitions already over quota, can leave a tail unplaced. It goes to the last
        // partition rather than nowhere: an unlabelled group is invisible to partition filtering.
        for (; cursor < items.Count; cursor++)
            assignments[items[cursor]] = labels[^1];

        return assignments;
    }
}
