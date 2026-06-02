namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Pure helper for the persistent partition labels (A, B, C…) carried by
/// <c>AcademicGroup.RotationGroup</c>. Existing labels are always preserved;
/// only groups without one are filled, balancing into the smallest partition.
/// Shared by partition assignment and schedule auto-arrange so the labelling
/// rule lives in exactly one place.
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
    /// Assigns a label to each currently-unlabelled item, in the supplied order,
    /// always filling the smallest partition first. Returns the new labels only
    /// for the items that lacked one — already-labelled items are left untouched.
    /// </summary>
    public static Dictionary<TId, string> AssignUnlabelled<TId>(
        IReadOnlyList<(TId Id, string? Label)> itemsInOrder,
        int requestedCount)
        where TId : notnull
    {
        var labels = BuildLabels(itemsInOrder.Select(i => i.Label), requestedCount);
        var counts = labels.ToDictionary(l => l, l => itemsInOrder.Count(i => i.Label == l));

        var assignments = new Dictionary<TId, string>();
        foreach (var item in itemsInOrder.Where(i => i.Label is null))
        {
            var label = counts.MinBy(kvp => kvp.Value).Key;
            assignments[item.Id] = label;
            counts[label]++;
        }

        return assignments;
    }
}
