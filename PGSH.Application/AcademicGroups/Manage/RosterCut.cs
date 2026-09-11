namespace PGSH.Application.AcademicGroups.Manage;

/// <summary>
/// How many rosters a promotion is cut into, and how many students each one holds.
///
/// <para>Pure arithmetic — no store, no clock — so the awkward cases can be walked exhaustively
/// rather than argued about. Same shape and same reason as <c>RotationTiling</c>.</para>
/// </summary>
/// <remarks>
/// <para>⚠ <b>« Également » veut dire plus grand reste, jamais <c>Skip</c>/<c>Take</c>.</b> Taking
/// <i>size</i> students at a time leaves the remainder in the last group: measured on screen
/// 2026-09-10, the 4ᵉ Pharmacie's 232 inscriptions in size 20 gave **eleven groups of 20 and one of
/// 12**. That runt then enters the rotation as a whole cohorte — it occupies a service's place for
/// 60 % of a group, and the printed répartition shows a column that does not balance.</para>
///
/// <para>The rule below gives 4 × 20 + 8 × 19 for the same request: the same number of rosters, every
/// one of them within one student of every other.</para>
///
/// <para>⚠ <b>And it deals them <c>20, 19, 19, 20, 19, 19…</c> rather than the four 20s first</b>, for
/// a reason that lives one act later: partitions are cut from <i>blocks of roster numbers</i>, so
/// grouping the larger rosters at the front hands them all to the same partitions. The balance this
/// class won inside a roster was being given straight back between columns — see
/// <see cref="ByCount"/>.</para>
/// </remarks>
internal static class RosterCut
{
    /// <summary>
    /// The size of each roster when the operator names a <b>maximum size</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The count is unchanged — only the shape is.</b> The number of rosters is still
    /// ⌈members ÷ size⌉, exactly as before, so nothing about a promotion's partitioning moves; what
    /// changes is that the students are spread evenly across them instead of piling into the first
    /// ones. The screen calls this field « Nombre <b>maximum</b> d'étudiants par groupe », and a
    /// balanced cut honours that maximum just as well while removing the runt.
    /// </remarks>
    public static IReadOnlyList<int> BySize(int members, int size)
    {
        if (members <= 0 || size <= 0) return [];

        int count = (members + size - 1) / size;
        return ByCount(members, count);
    }

    /// <summary>
    /// The size of each roster when the operator names <b>how many rosters</b> — « la 5ᵉ MED en 100
    /// groupes ». Largest remainder: the sizes differ by at most one, and they sum to
    /// <paramref name="members"/>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Asked for more rosters than there are students, it returns one per student and no empty
    /// ones. A roster with nobody in it is not a smaller roster — it is a row the répartition would
    /// carry through every stage of the year for nothing.</para>
    ///
    /// <para>⚠ <b>And the larger rosters are spread through the sequence, never grouped at the
    /// front.</b> The sizes are the same either way and so is the count; what the order decides is the
    /// <i>next</i> act. <c>PartitionAllocator.Contiguous</c> — the strategy the faculty uses, and the
    /// only one the register shows chosen, 8 times out of 8 — gives partition A the first block of
    /// roster numbers, B the next, and so on. Emitted biggest-first, every oversized roster therefore
    /// landed in the leading partitions: measured on the live base 2026-09-11, the 3ᵉ MED's 933
    /// inscriptions in 100 rosters (33 of 10, then 67 of 9) cut into ten partitions of
    /// <b>100, 100, 100, 93, 90, 90, 90, 90, 90, 90</b>. Dealt as below the same cut gives
    /// <b>94, 94, 94, 93 ×7</b>.</para>
    ///
    /// <para>⚠ <b>The spread is not cosmetic — a partition is a column, and a column is what a service
    /// holds at one instant.</b> Santé Publique and Simulation Médicale each offer 100 places for an
    /// average column of 94, which reads as six to spare; the three columns of 100 spent all six, so
    /// both stages sat at exactly their ceiling with nothing on any screen saying so. A single late
    /// arrival or one changement de groupe into A, B or C put them over.</para>
    /// </remarks>
    public static IReadOnlyList<int> ByCount(int members, int count)
    {
        if (members <= 0 || count <= 0) return [];

        count = Math.Min(count, members);

        int floor = members / count;
        int larger = members % count;

        // Which positions carry the extra student. `(i · larger) mod count < larger` holds for exactly
        // `larger` of the `count` positions — whatever their common divisor — and spaces them as evenly
        // as integers allow, starting at the first. Nothing downstream depends on the order beyond
        // this: the handler walks the sizes with a running offset, so any permutation places the same
        // students.
        return [.. Enumerable.Range(0, count).Select(i => i * larger % count < larger ? floor + 1 : floor)];
    }

    /// <summary>
    /// Splits a target number of rosters between the CNPN texts present at a level, proportionally to
    /// how many students each text holds.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>A count cannot simply be divided once.</b> Groups never mix texts — a roster rotates
    /// through one stage set together — so each text takes <b>whole</b> rosters of its own, and « 100 »
    /// has to be apportioned before anything is cut. Largest remainder again, on the fractional parts.</para>
    ///
    /// <para>⚠ <b>Every text with students gets at least one roster, and never more rosters than it
    /// has students.</b> Either bound can move the total away from what was asked — a text of 7
    /// students cannot yield 12 rosters — which is why the handler reports the number actually created
    /// beside the number requested rather than letting the difference pass as arithmetic.</para>
    /// </remarks>
    public static IReadOnlyList<int> Apportion(IReadOnlyList<int> bucketSizes, int totalCount)
    {
        if (bucketSizes.Count == 0) return [];
        if (bucketSizes.Count == 1) return [Math.Min(totalCount, bucketSizes[0])];

        int population = bucketSizes.Sum();
        if (population <= 0 || totalCount <= 0) return [.. bucketSizes.Select(_ => 0)];

        var exact = bucketSizes.Select(m => (double)totalCount * m / population).ToList();
        var shares = exact.Select(e => (int)Math.Floor(e)).ToList();

        int remaining = totalCount - shares.Sum();

        // The seats left over go to the largest fractional parts — the standard tie-break, and the
        // one that keeps a small text from being rounded out of existence by a large one.
        foreach (int i in Enumerable.Range(0, shares.Count)
                     .OrderByDescending(i => exact[i] - shares[i])
                     .ThenBy(i => i)
                     .Take(Math.Max(0, remaining)))
            shares[i]++;

        for (int i = 0; i < shares.Count; i++)
            shares[i] = Math.Clamp(shares[i], bucketSizes[i] > 0 ? 1 : 0, bucketSizes[i]);

        return shares;
    }
}
