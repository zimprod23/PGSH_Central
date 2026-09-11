namespace PGSH.Domain.Stages;

/// <summary>One stage's weight on a promotion's axis, in columns.</summary>
/// <param name="Periods">
/// <c>kₛ</c> — how many columns of the axis a partition spends in this stage. Derived from the
/// stage's duration, not authored: the panel that reads this exists precisely because no axis has
/// been laid yet.
/// </param>
public sealed record AxisWeight(int StageId, int DurationInDays, int Periods);

/// <param name="Timeline"><c>T = Σkₛ</c> — the columns a partition needs to visit every stage.</param>
/// <param name="ColumnDays">
/// How long one column lasts. The gcd of the durations, so a 30-day stage is exactly two columns of
/// a 15-day axis — the condition that lets stages of different lengths share one axis.
/// </param>
/// <param name="Unweighted">
/// Stages carrying no usable duration, which therefore sit on no axis at all. ⚠ Reported rather than
/// dropped: a stage missing from the arithmetic makes every other stage's share look *larger*, so
/// silence here would print a promotion as fitting because one of its stages was invisible.
/// </param>
public sealed record PromotionAxisPlan(
    IReadOnlyList<AxisWeight> Weights,
    int Timeline,
    int ColumnDays,
    IReadOnlyList<int> Unweighted);

/// <summary>
/// The arithmetic of « combien d'étudiants sont dans ce stage <b>en même temps</b> », from durations
/// alone.
///
/// <para><b>Why it can be answered before anything is planned.</b> A promotion's year is its stages
/// laid end to end on one shared axis: stage <c>s</c> holds a partition for <c>kₛ</c> columns, so a
/// partition needs <c>T = Σkₛ</c> columns to visit them all, and at any instant the promotion is
/// spread across all of them at once. The slice standing in one stage is therefore <c>N·kₛ/T</c> —
/// which, since <c>kₛ</c> is proportional to the duration, is <c>N × durée_s ÷ Σdurées</c>. No
/// roster, no axis and no cell is needed to compute it; headcount and the catalogue are enough.</para>
///
/// <para>⚠ <b>The partition count cancels out and that is the point.</b> Cutting the promotion into
/// more groups moves the same students through the same stages in smaller pieces — it cannot relieve
/// a stage whose services do not hold <c>N·kₛ/T</c>. Same identity as
/// <c>RotationCyclePlanner</c>'s <c>Lₛ = P·kₛ/T</c>, read per student instead of per partition.</para>
///
/// <para>Pure — no store, no clock — like <see cref="StageScoring"/> and
/// <c>RotationTiling</c>, and for the same reason: the arithmetic is exact, so its cases should be
/// exact too rather than approximately seeded.</para>
/// </summary>
public static class PromotionAxis
{
    /// <summary>
    /// The axis the promotion's stages imply. ⚠ <paramref name="stages"/> is the <b>whole</b>
    /// promotion: a share is a fraction of the year, so leaving a stage out inflates every other
    /// stage's share rather than merely omitting a row.
    /// </summary>
    public static PromotionAxisPlan Lay(IReadOnlyCollection<(int StageId, int DurationInDays)> stages)
    {
        // A duration of zero or less puts a stage nowhere on the axis — it is a catalogue gap, and
        // the caller is told which stages so it can say so rather than quietly dividing by a total
        // that is missing them.
        var weighted = stages.Where(s => s.DurationInDays > 0).ToList();
        var unweighted = stages.Where(s => s.DurationInDays <= 0).Select(s => s.StageId).ToList();

        if (weighted.Count == 0)
            return new PromotionAxisPlan([], 0, 0, unweighted);

        int columnDays = weighted.Select(s => s.DurationInDays).Aggregate(Gcd);

        var weights = weighted
            .Select(s => new AxisWeight(s.StageId, s.DurationInDays, s.DurationInDays / columnDays))
            .ToList();

        return new PromotionAxisPlan(weights, weights.Sum(w => w.Periods), columnDays, unweighted);
    }

    /// <summary>
    /// How many of <paramref name="students"/> stand in a stage of weight <paramref name="periods"/>
    /// at one time.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Rounded up.</b> A promotion does not divide evenly into columns, and the remainder is a
    /// real student who has to be somewhere: 933 over ten columns is 94 in the fullest one, not 93.3.
    /// Rounding down would understate every shortfall by up to one place per stage — and a shortfall
    /// understated is the reading that lets a publication be attempted.
    /// </remarks>
    public static int SimultaneousStudents(int students, int periods, int timeline) =>
        timeline <= 0 || periods <= 0 || students <= 0
            ? 0
            : (int)Math.Ceiling((decimal)students * periods / timeline);

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
