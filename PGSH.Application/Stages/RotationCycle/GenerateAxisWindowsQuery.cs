using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>How a column's length is stated.</summary>
public enum AxisColumnUnit
{
    /// <summary>
    /// Calendar months, ends inclusive: 1ᵉʳ septembre → 30 septembre. What the faculty's own tables use,
    /// and what keeps a ten-column axis landing on the first of each month.
    /// </summary>
    Months,

    /// <summary>
    /// Runs of seven calendar days. One to four weeks per column is the common request — a block of
    /// two-week columns is how a semester of short placements is laid out.
    /// </summary>
    Weeks,

    /// <summary>
    /// Jours ouvrables: the column holds exactly this many worked days, and its end date is wherever that
    /// falls once weekends and declared holidays are skipped. The only unit under which two columns of the
    /// same stated length are actually the same amount of stage.
    /// </summary>
    WorkingDays,
}

/// <summary>
/// Lays a block's shared axis out from one start date — the whole point of the rotation-cycle screen, moved
/// server-side because the browser cannot know the holidays.
/// </summary>
/// <remarks>
/// ⚠ It used to be done in the page, with <c>setUTCMonth</c>. That was correct for calendar months and
/// silently wrong the moment "duration" means worked days: no client has the holiday table, so a window
/// generated there is a window that counted Aïd as four days of stage.
/// </remarks>
/// <param name="LevelId">
/// The promotion the axis is being laid for. ⚠ <b>Supply it.</b> Omitted, the columns are laid on the
/// faculty calendar alone and step over no exam week — and an axis authored that way puts the grid and
/// the rotations published from it out of agreement with the promotion's own calendar from the first
/// day. Null is accepted only because « quelles fenêtres feraient dix colonnes d'un mois » is a
/// legitimate question with no promotion behind it.
/// </param>
public sealed record GenerateAxisWindowsQuery(
    int Columns,
    DateOnly StartDate,
    AxisColumnUnit Unit = AxisColumnUnit.Months,
    int Length = 1,
    int? LevelId = null,
    int? AcademicYearId = null) : IQuery<GeneratedAxisResponse>;

/// <param name="Holidays">The faculty's own closures inside the column — jours fériés, vacances.</param>
/// <param name="Pauses">
/// The promotion's own windows inside it — an exam session. Reported apart from
/// <paramref name="Holidays"/> because they are not the same fact: a holiday is everyone's, a pause is
/// this promotion's, and the promotion rotating through the same service the same morning does not have
/// it. Always empty when the query named no promotion.
/// </param>
public sealed record GeneratedAxisColumn(
    int Number,
    DateOnly StartDate,
    DateOnly EndDate,
    int CalendarDays,
    int WorkingDays,
    IReadOnlyList<string> Holidays,
    bool HasProvisionalDates,
    IReadOnlyList<string> Pauses);

/// <param name="CalendarIsEmpty">
/// No holiday is recorded anywhere in the span, so every count below is calendar days minus weekends. On a
/// fresh base that is the normal state and it makes « jours ouvrables » quietly mean something narrower
/// than it says — hence a flag rather than a silent best effort.
/// </param>
public sealed record GeneratedAxisResponse(
    IReadOnlyList<GeneratedAxisColumn> Columns,
    int WorkingDaysTotal,
    int CalendarDaysTotal,
    bool CalendarIsEmpty,
    IReadOnlyList<string> MissingReligious,
    IReadOnlyList<string> Warnings);

internal sealed class GenerateAxisWindowsQueryValidator : AbstractValidator<GenerateAxisWindowsQuery>
{
    public GenerateAxisWindowsQueryValidator()
    {
        RuleFor(x => x.Columns).InclusiveBetween(1, 60);
        RuleFor(x => x.Unit).IsInEnum();

        RuleFor(x => x.Length).InclusiveBetween(1, 12)
            .When(x => x.Unit != AxisColumnUnit.WorkingDays)
            .WithMessage("Une colonne fait de 1 à 12 mois ou semaines.");

        RuleFor(x => x.Length).InclusiveBetween(1, 260)
            .When(x => x.Unit == AxisColumnUnit.WorkingDays)
            .WithMessage("Une colonne fait de 1 à 260 jours ouvrables.");
    }
}

internal sealed class GenerateAxisWindowsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    WorkingDayProvider workingDays)
    : IQueryHandler<GenerateAxisWindowsQuery, GeneratedAxisResponse>
{
    public async Task<Result<GeneratedAxisResponse>> Handle(
        GenerateAxisWindowsQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<GeneratedAxisResponse>(year.Error);

        // ⚠ The promotion's calendar, not the faculty's: the whole point of a promotion pause is that
        // the columns laid here step over it, so that the grid and the périodes published from it are
        // laid against the same days from the start. A query naming no level gets the faculty calendar.
        var calendar = await workingDays.ForPromotionAsync(year.Value, request.LevelId, cancellationToken);

        var windows = request.Unit == AxisColumnUnit.WorkingDays
            ? calendar.LaySeries(request.StartDate, request.Columns, request.Length)
                .Select(w => (w.Start, w.End))
                .ToList()
            : Calendrical(request.StartDate, request.Columns, request.Unit, request.Length);

        if (windows.Count < request.Columns)
            return Result.Failure<GeneratedAxisResponse>(RotationCycleErrors.AxisDoesNotFit(
                request.Columns, windows.Count));

        var columns = windows
            .Select((w, i) => Column(i + 1, w.Start, w.End, calendar))
            .ToList();

        var span = (From: columns[0].StartDate, To: columns[^1].EndDate);

        return new GeneratedAxisResponse(
            columns,
            columns.Sum(c => c.WorkingDays),
            columns.Sum(c => c.CalendarDays),
            calendar.HolidaysBetween(span.From, span.To)
                .All(c => c.Scope != CalendarClosureScope.Faculty),
            // Asked of the whole Gregorian years the axis touches, never of the axis span: a lunar date
            // drifts ~11 days a year, so an autumn axis would otherwise report every spring holiday
            // "missing" and send the user hunting for rows that are already on file.
            calendar.MissingReligious(span.From, span.To),
            Warnings(columns, request, await SpansYear(span, cancellationToken)));
    }

    private static GeneratedAxisColumn Column(
        int number, DateOnly start, DateOnly end, WorkingDayCalendar calendar)
    {
        var closures = calendar.HolidaysBetween(start, end);

        return new GeneratedAxisColumn(
            number,
            start,
            end,
            end.DayNumber - start.DayNumber + 1,
            calendar.Count(start, end),
            closures.Where(c => c.Scope == CalendarClosureScope.Faculty).Select(c => c.Name).ToList(),
            closures.Any(c => !c.IsConfirmed),
            closures.Where(c => c.Scope == CalendarClosureScope.Promotion).Select(c => c.Name).ToList());
    }

    /// <summary>
    /// Calendar months or weeks: contiguous and inclusive of both ends, the convention
    /// <c>SlotOverlapGuard</c> enforces — the next column starts the day after the last one ends.
    /// </summary>
    private static List<(DateOnly Start, DateOnly End)> Calendrical(
        DateOnly start, int columns, AxisColumnUnit unit, int length)
    {
        var windows = new List<(DateOnly, DateOnly)>(columns);
        var cursor = start;

        for (int i = 0; i < columns; i++)
        {
            var end = unit == AxisColumnUnit.Months
                ? cursor.AddMonths(length).AddDays(-1)
                : cursor.AddDays(length * 7 - 1);

            windows.Add((cursor, end));
            cursor = end.AddDays(1);
        }

        return windows;
    }

    private async Task<bool> SpansYear((DateOnly From, DateOnly To) span, CancellationToken ct)
    {
        var current = await dbContext.AcademicYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.IsCurrent, ct);

        return current is not null && (span.From < current.StartDate || span.To > current.EndDate);
    }

    /// <summary>
    /// Legal but probably unintended — never blocking, the same contract as
    /// <c>RotationCycleLayout.Warnings</c>. The faculty decides; we make sure it is looking.
    /// </summary>
    private static List<string> Warnings(
        IReadOnlyList<GeneratedAxisColumn> columns, GenerateAxisWindowsQuery request, bool spansYear)
    {
        var warnings = new List<string>();

        if (spansYear)
            warnings.Add("L'axe sort des dates de l'année universitaire courante.");

        // Under Months, February and August are not the same amount of stage. Under WorkingDays the count
        // is fixed by construction, so a spread there would be a bug rather than a fact about calendars.
        int min = columns.Min(c => c.WorkingDays);
        int max = columns.Max(c => c.WorkingDays);

        if (request.Unit != AxisColumnUnit.WorkingDays && max - min >= 3)
            warnings.Add(
                $"Les colonnes vont de {min} à {max} jours ouvrables — {max - min} jours d'écart entre "
                + "la plus courte et la plus longue. Exprimez la durée en jours ouvrables pour les égaliser.");

        if (columns.Any(c => c.HasProvisionalDates))
            warnings.Add(
                "Certaines colonnes contiennent une fête religieuse encore provisoire : les dates "
                + "bougeront si le décret la déplace.");

        if (columns.Any(c => c.WorkingDays == 0))
            warnings.Add("Une colonne ne contient aucun jour ouvrable.");

        // Said only when a window was actually stepped over — the columns are already correct, and the
        // point of saying so is that the dates will not match a table drawn on the faculty calendar.
        int pausedColumns = columns.Count(c => c.Pauses.Count > 0);
        if (pausedColumns > 0)
            warnings.Add(
                $"{pausedColumns} colonne(s) enjambent une suspension de la promotion "
                + $"({string.Join(", ", columns.SelectMany(c => c.Pauses).Distinct())}) : "
                + "elles sont plus longues sur le calendrier que leur durée en jours ouvrables.");

        return warnings;
    }
}
