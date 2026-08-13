namespace PGSH.Domain.Calendar;

/// <summary>
/// The fixed-date Moroccan public holidays, generated per Gregorian year so nobody types them.
///
/// <para>⚠ <b>Only half the holidays are here.</b> The religious ones — Aïd al-Fitr, Aïd al-Adha,
/// 1ᵉʳ Moharram, Aïd al-Mawlid — follow the Hijri calendar, and in Morocco the month turns on observation
/// of the crescent with the days off announced by decree. They are not computable, only enterable, and a
/// calendar without them under-counts a stage by up to six days a year. That is why
/// <c>GetHolidayCoverageQuery</c> reports how many religious days are recorded per year rather than
/// leaving their absence to be discovered from a wrong end date.</para>
/// </summary>
public static class MoroccanPublicHolidays
{
    /// <summary>
    /// Nouvel An Amazigh (14 janvier) became a paid public holiday by the décret of May 2023, first
    /// observed in 2024 — so generating it for an earlier year would invent a day off that was worked.
    /// </summary>
    private const int AmazighNewYearFirstObserved = 2024;

    private static readonly (int Month, int Day, string Name)[] Fixed =
    [
        (1, 1, "Nouvel An"),
        (1, 11, "Manifeste de l'Indépendance"),
        (5, 1, "Fête du Travail"),
        (7, 30, "Fête du Trône"),
        (8, 14, "Journée de Oued Eddahab"),
        (8, 20, "Révolution du Roi et du Peuple"),
        (8, 21, "Fête de la Jeunesse"),
        (11, 6, "Marche Verte"),
        (11, 18, "Fête de l'Indépendance"),
    ];

    /// <summary>
    /// The fixed national days falling in <paramref name="gregorianYear"/>, each a single confirmed day.
    /// </summary>
    public static IReadOnlyList<Holiday> FixedFor(int gregorianYear)
    {
        var days = Fixed.ToList();

        if (gregorianYear >= AmazighNewYearFirstObserved)
            days.Add((1, 14, "Nouvel An Amazigh"));

        return days
            .OrderBy(d => d.Month).ThenBy(d => d.Day)
            .Select(d =>
            {
                var date = new DateOnly(gregorianYear, d.Month, d.Day);
                return new Holiday
                {
                    StartDate = date,
                    EndDate = date,
                    Name = d.Name,
                    Kind = HolidayKind.National,
                    IsConfirmed = true,
                };
            })
            .ToList();
    }

    /// <summary>
    /// The religious holidays a complete year needs, as names and their usual length. Not dates — the
    /// point is that PGSH cannot supply those. Used to tell the user what is missing.
    /// </summary>
    public static IReadOnlyList<(string Name, int UsualDayCount)> ExpectedReligious =>
    [
        ("Aïd al-Fitr", 2),
        ("Aïd al-Adha", 2),
        ("1ᵉʳ Moharram", 1),
        ("Aïd al-Mawlid", 1),
    ];
}
