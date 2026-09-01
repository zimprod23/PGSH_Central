namespace PGSH.Application.Stages.Cnpn;

/// <summary>
/// When a student entered his programme — the fact arrêté 1650.25 art. 2 keys the governing text on,
/// and the one the legacy import most often failed to record.
///
/// <para><b>Pure, and shared, because it is the single assumption the whole backfill rests on.</b>
/// The legacy base only carried a student once he had stages, so roughly 2,200 currently-enrolled
/// students have no registration before 2025-2026 even though they plainly did not start that year.
/// Where the earliest registration sits above the first level, entry is deduced by walking back
/// (level - 1) academic years — you cannot be in the third year without having spent two — and that
/// deduction decides which arrêté governs the student. Written twice it can disagree with itself
/// twice, which is why <see cref="CnpnAssignment"/> and <see cref="RegistrationCnpnStamper"/> both
/// come here rather than each carrying a copy.</para>
///
/// <para>No store and no clock, like <c>PeriodAxis</c>, <c>RotationTiling</c> and
/// <c>StagePeriodFolder</c>: the caller reads the years, this decides.</para>
/// </summary>
internal static class EntryYearDeduction
{
    /// <summary>An academic year reduced to what the deduction needs: identity and where it sits.</summary>
    internal sealed record AcademicYearRef(int Id, DateOnly StartDate);

    /// <summary>
    /// True when the earliest registration on record <i>is</i> the entry. A first registration at
    /// level 1 is a genuine entry; at any higher level it is only the first year PGSH happens to
    /// hold, and the real entry is earlier.
    /// </summary>
    internal static bool IsRecordedEntry(int levelYearAtEarliestRegistration) =>
        levelYearAtEarliestRegistration <= 1;

    /// <summary>
    /// The academic year the student entered on.
    /// </summary>
    /// <param name="yearsByStartDate">Every academic year, ordered by start date.</param>
    /// <param name="earliestKnownYearId">The year of the earliest registration PGSH holds.</param>
    /// <param name="levelYearAtEarliestRegistration">The level year he sat in that year.</param>
    /// <remarks>
    /// Falls back to the earliest known year when history does not reach far enough — which still
    /// lands before any modern CNPN, so the answer stays right even when the exact year does not.
    /// An unknown year id is returned unchanged for the same reason: a year we cannot place is a
    /// year we cannot walk back from, and inventing one would be worse than reading the one we have.
    /// </remarks>
    internal static int EntryYearId(
        IReadOnlyList<AcademicYearRef> yearsByStartDate,
        int earliestKnownYearId,
        int levelYearAtEarliestRegistration)
    {
        if (IsRecordedEntry(levelYearAtEarliestRegistration))
            return earliestKnownYearId;

        int index = IndexOf(yearsByStartDate, earliestKnownYearId);
        if (index < 0)
            return earliestKnownYearId;

        int walked = index - (levelYearAtEarliestRegistration - 1);
        return yearsByStartDate[Math.Max(0, walked)].Id;
    }

    private static int IndexOf(IReadOnlyList<AcademicYearRef> years, int yearId)
    {
        for (int i = 0; i < years.Count; i++)
            if (years[i].Id == yearId)
                return i;

        return -1;
    }
}
