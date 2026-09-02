using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Services.Occupancy;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.OccupancyReport;

/// <summary>
/// Builds the cross-service report from <b>three flat reads</b> and the same pure
/// <see cref="OccupancyTimeline"/> the per-service page uses.
///
/// <para>⚠ <b>One read for every service, never one read per service.</b> 148 services × a query
/// each is the shape that made a single « Générer le plan » ~700 round trips. The placements come
/// back once and are folded in memory.</para>
///
/// <para>⚠ <b>No collection subquery in any projection.</b> The occupants of a segment and the
/// quotas of a service are collections; folded into a row projection they are the element with no
/// key that Npgsql refuses — the shape that killed the macro plan with the whole suite green. The
/// quotas ride on an <c>Include</c> (a navigation, not a projected collection) and the placements
/// are their own top-level query.</para>
/// </summary>
internal sealed class GetOccupancyReportQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetOccupancyReportQuery, OccupancyReportResponse>
{
    /// <summary>
    /// Above this the report stops being a document. A year of the real base is ~7 500 cells, so it
    /// bites only on a caller that has found a way past a single year.
    /// </summary>
    internal const int MaxPlacements = 60_000;

    public async Task<Result<OccupancyReportResponse>> Handle(
        GetOccupancyReportQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<OccupancyReportResponse>(year.Error);

        var window = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == year.Value)
            .Select(y => new { y.Label, y.StartDate, y.EndDate })
            .FirstAsync(cancellationToken);

        var services = await ServicesQuery(dbContext, request.HospitalId)
            .ToListAsync(cancellationToken);

        if (services.Count == 0)
            return Result.Failure<OccupancyReportResponse>(OccupancyReportErrors.NoServicesInScope);

        var serviceIds = services.Select(s => s.Id).ToHashSet();

        int placementCount = await PlacementsQuery(dbContext, window.StartDate, window.EndDate)
            .CountAsync(cancellationToken);

        if (placementCount > MaxPlacements)
            return Result.Failure<OccupancyReportResponse>(
                OccupancyReportErrors.TooManyPlacements(placementCount, MaxPlacements));

        // ⚠ Every placement of the year, not only the filtered ones. A saturation verdict is about
        // the service, and the ceiling that refuses a publish counts every promotion standing in it
        // — measuring « la 5ᵉ année seule » against the service total prints « ok » for a service
        // that is over because of the 3ᵉ.
        var placements = (await PlacementsQuery(dbContext, window.StartDate, window.EndDate)
                .ToListAsync(cancellationToken))
            .Where(p => serviceIds.Contains(p.ServiceId))
            .ToList();

        var allowedByStage = await AllowedServicesQuery(dbContext).ToListAsync(cancellationToken);

        bool Attributed(ReportPlacement p) =>
            (request.LevelId is not { } level || p.LevelId == level)
            && (request.StageId is not { } stage || p.StageId == stage);

        var rows = services
            .Select(service => BuildServiceRow(
                service,
                placements.Where(p => p.ServiceId == service.Id).ToList(),
                Attributed))
            .ToList();

        // A filter picks which services are *listed*: one holding nobody from the promotion asked
        // about is not part of that promotion's answer, and listing it drowns the ones that are.
        // A never-used service survives the filter — that it is empty is the finding.
        bool filtered = request.LevelId is not null || request.StageId is not null;

        var listed = rows
            .Where(r => !filtered || r.Share > 0 || r.SegmentCount == 0)
            .Where(r => !request.OnlySaturated || r.OverCapacitySegments > 0)
            // A null saturation sorts first, and deliberately: it means there is no ceiling to divide
            // by, which on a restricted service is « accueille une promotion qu'il n'admet pas » —
            // the one refusal publication cannot force. Above even a service at 400 %.
            .OrderByDescending(r => r.Saturation ?? decimal.MaxValue)
            .ThenByDescending(r => r.PeakStudents)
            .ThenBy(r => r.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var attributed = placements.Where(Attributed).ToList();

        var facultyTimeline = OccupancyTimeline.Build(attributed.Select(ToPlacement));

        // ⚠ Every segment that reaches the peak, not the first one that does. A plateau held from
        // September to March is dozens of consecutive segments; `MaxBy` returns one of them, and
        // reporting its window announced a month-long peak for a six-month one.
        int peakLoad = facultyTimeline.Count == 0
            ? 0
            : facultyTimeline.Max(seg => seg.Occupants.Sum(o => o.Students));

        var peakSegments = facultyTimeline
            .Where(seg => seg.Occupants.Sum(o => o.Students) == peakLoad)
            .ToList();

        var totals = new OccupancyReportTotals(
            ServicesInScope:         rows.Count,
            ServicesOccupied:        rows.Count(r => r.SegmentCount > 0),
            ServicesNeverUsed:       rows.Count(r => r.SegmentCount == 0),
            ServicesOverCapacity:    rows.Count(r => r.OverCapacitySegments > 0),
            ServicesAdmittingNobody: rows.Count(r => r.LevelsNotAdmitted.Count > 0),
            PlacementCount:          attributed.Count,
            PlacedStudents:          attributed.Sum(p => p.Students),
            DistinctStages:          attributed.Select(p => p.StageId).Distinct().Count(),
            DistinctLevels:          attributed.Select(p => p.LevelId).Distinct().Count(),
            PeakStudents:            peakLoad,
            PeakStart:               peakSegments.Count == 0 ? null : peakSegments.Min(seg => seg.StartDate),
            PeakEnd:                 peakSegments.Count == 0 ? null : peakSegments.Max(seg => seg.EndDate),
            PeakDays:                peakSegments.Sum(seg => seg.EndDate.DayNumber - seg.StartDate.DayNumber + 1),
            ServiceDaysOverCapacity: rows.Sum(r => r.DaysOverCapacity));

        var response = new OccupancyReportResponse(
            year.Value,
            window.Label,
            window.StartDate,
            window.EndDate,
            Scope(services, request, window.Label),
            totals,
            MonthBars(facultyTimeline, rows),
            listed,
            StageRows(attributed, allowedByStage),
            LevelRows(attributed, rows),
            Notes(rows, totals, services));

        return Result.Success(response);
    }

    // ── Per service ────────────────────────────────────────────────────────────────────────────

    private static OccupancyServiceRow BuildServiceRow(
        Service service, List<ReportPlacement> own, Func<ReportPlacement, bool> attributed)
    {
        var segments = OccupancyTimeline.Build(own.Select(ToPlacement));
        bool restricted = service.HasLevelRestrictions;

        var bands = segments.Select(segment =>
        {
            int students = segment.Occupants.Sum(o => o.Students);

            // The same branch SchedulePublisher.EnsureIntakeAsync takes, so the report and the
            // refusal it exists to explain cannot disagree: a restricted service is measured per
            // promotion against that promotion's quota, an unrestricted one on everybody at once.
            int overflow = restricted
                ? segment.Occupants
                    .GroupBy(o => o.LevelId)
                    .Sum(g => Math.Max(0, g.Sum(o => o.Students) - service.CapacityFor(g.Key)))
                : Math.Max(0, students - service.Capacity);

            return new OccupancyBand(
                segment.StartDate,
                segment.EndDate,
                segment.EndDate.DayNumber - segment.StartDate.DayNumber + 1,
                students,
                restricted ? null : service.Capacity,
                overflow);
        }).ToList();

        var peak = bands.MaxBy(b => b.Students);

        var levels = own
            .GroupBy(p => (p.LevelId, p.LevelLabel))
            .Select(g => new OccupancyServiceLevel(
                g.Key.LevelId,
                g.Key.LevelLabel,
                // The promotion's own peak, read off the segments rather than summed over the year:
                // a promotion passing through in three separate windows is never all there at once.
                PeakFor(segments, g.Key.LevelId),
                restricted ? service.CapacityFor(g.Key.LevelId) : null,
                !service.Admits(g.Key.LevelId)))
            .OrderByDescending(l => l.PeakStudents)
            .ToList();

        // ⚠ On a restricted service there is no single ceiling: the sum of the quotas of the
        // promotions actually present is the only honest denominator, and it is 0 for a promotion
        // the service does not admit at all.
        int ceiling = restricted ? levels.Sum(l => l.Capacity ?? 0) : service.Capacity;

        var stages = own
            .GroupBy(p => (p.StageId, p.StageName, p.LevelLabel))
            .Select(g => new OccupancyServiceStage(
                g.Key.StageId,
                g.Key.StageName,
                g.Key.LevelLabel,
                g.Select(p => p.CellId).Distinct().Count(),
                g.Sum(p => p.Students)))
            .OrderByDescending(s => s.Students)
            .ToList();

        return new OccupancyServiceRow(
            service.Id,
            service.Name,
            service.HospitalId,
            service.Hospital.Name,
            service.Hospital.City ?? "",
            restricted ? CapacityRule.PerLevel : CapacityRule.Total,
            ceiling,
            service.Capacity,
            service.LevelCapacities
                .OrderBy(c => c.Level.AcademicProgram)
                .ThenBy(c => c.Level.Year)
                .Select(c => new LevelQuotaResponse(
                    c.LevelId, c.Level.Label ?? $"niveau {c.LevelId}", c.Capacity))
                .ToList(),
            segments.Count,
            peak?.Students ?? 0,
            peak?.StartDate,
            peak?.EndDate,
            // Null, never 0: a service with no ceiling to divide by sorts as « the least saturated »
            // under a 0, which is exactly wrong for one that admits nobody.
            ceiling > 0 ? Math.Round((decimal)(peak?.Students ?? 0) / ceiling, 2) : null,
            bands.Count(b => b.Overflow > 0),
            bands.Where(b => b.Overflow > 0).Sum(b => b.Days),
            levels.Where(l => l.NotAdmitted).Select(l => l.LevelLabel).ToList(),
            own.Where(attributed).Sum(p => p.Students),
            bands,
            levels,
            stages);
    }

    private static int PeakFor(List<OccupancySegment> segments, int levelId) =>
        segments.Count == 0
            ? 0
            : segments.Max(s => s.Occupants.Where(o => o.LevelId == levelId).Sum(o => o.Students));

    // ── Faculty-wide ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The month's <b>peak</b>, not its mean: an average over a month in which one week is saturated
    /// reads comfortable, and the week is the thing somebody has to act on. Read off the exact
    /// segments, so it is the real maximum reached inside the month rather than a sample of it.
    /// </summary>
    private static List<OccupancyMonthBar> MonthBars(
        List<OccupancySegment> faculty, List<OccupancyServiceRow> rows)
    {
        var peaks = new SortedDictionary<(int Year, int Month), OccupancySegment>();
        var occupied = new Dictionary<(int Year, int Month), HashSet<int>>();
        var over = new Dictionary<(int Year, int Month), HashSet<int>>();

        // The peak *segment* is kept, not just its number: the promotion split has to come from that
        // same moment or the parts do not add up to the whole, and a stacked bar that does not add
        // up is worse than no bar.
        foreach (var segment in faculty)
        {
            int students = segment.Occupants.Sum(o => o.Students);

            foreach (var key in MonthsSpanned(segment.StartDate, segment.EndDate))
            {
                if (!peaks.TryGetValue(key, out var best)
                    || students > best.Occupants.Sum(o => o.Students))
                {
                    peaks[key] = segment;
                }
            }
        }

        foreach (var row in rows)
        {
            foreach (var band in row.Bands)
            {
                foreach (var key in MonthsSpanned(band.StartDate, band.EndDate))
                {
                    if (!peaks.ContainsKey(key))
                        continue;

                    if (!occupied.TryGetValue(key, out var occupiedHere))
                        occupied[key] = occupiedHere = [];

                    occupiedHere.Add(row.ServiceId);

                    if (band.Overflow <= 0)
                        continue;

                    if (!over.TryGetValue(key, out var overHere))
                        over[key] = overHere = [];

                    overHere.Add(row.ServiceId);
                }
            }
        }

        return peaks
            .Select(m => new OccupancyMonthBar(
                m.Key.Year,
                m.Key.Month,
                MonthLabel(m.Key.Year, m.Key.Month),
                m.Value.Occupants.Sum(o => o.Students),
                occupied.GetValueOrDefault(m.Key)?.Count ?? 0,
                over.GetValueOrDefault(m.Key)?.Count ?? 0,
                m.Value.Occupants
                    .GroupBy(o => (o.LevelId, o.LevelLabel))
                    .Select(g => new MonthLevelLoad(g.Key.LevelId, g.Key.LevelLabel, g.Sum(o => o.Students)))
                    .OrderByDescending(l => l.Students)
                    .ToList()))
            .ToList();
    }

    private static IEnumerable<(int Year, int Month)> MonthsSpanned(DateOnly start, DateOnly end)
    {
        for (var cursor = new DateOnly(start.Year, start.Month, 1); cursor <= end; cursor = cursor.AddMonths(1))
            yield return (cursor.Year, cursor.Month);
    }

    private static readonly string[] MonthNames =
        ["janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc."];

    private static string MonthLabel(int year, int month) => $"{MonthNames[month - 1]} {year}";

    private static List<OccupancyStageRow> StageRows(
        List<ReportPlacement> placements, List<StageAllowedCount> allowed)
    {
        var allowedById = allowed.ToDictionary(a => a.StageId, a => a.AllowedServices);

        return placements
            .GroupBy(p => (p.StageId, p.StageName, p.LevelId, p.LevelLabel))
            .Select(g =>
            {
                int used = g.Select(p => p.ServiceId).Distinct().Count();
                int declared = allowedById.GetValueOrDefault(g.Key.StageId);

                // The highest *simultaneous* load this stage puts in one service — not its yearly
                // total, which counts a service reused in three windows as three times the pressure.
                var heaviest = g
                    .GroupBy(p => (p.ServiceId, p.ServiceName))
                    .Select(s => new { s.Key.ServiceName, Load = Peak(s) })
                    .MaxBy(s => s.Load);

                return new OccupancyStageRow(
                    g.Key.StageId,
                    g.Key.StageName,
                    g.Key.LevelId,
                    g.Key.LevelLabel,
                    declared,
                    used,
                    Math.Max(0, declared - used),
                    g.Select(p => p.CellId).Distinct().Count(),
                    g.Sum(p => p.Students),
                    heaviest?.Load ?? 0,
                    heaviest?.ServiceName);
            })
            .OrderByDescending(s => s.ServicesUnused)
            .ThenByDescending(s => s.PlacedStudents)
            .ToList();
    }

    private static List<OccupancyLevelRow> LevelRows(
        List<ReportPlacement> placements, List<OccupancyServiceRow> rows)
    {
        var notAdmitting = rows
            .SelectMany(r => r.Levels.Where(l => l.NotAdmitted).Select(l => l.LevelId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return placements
            .GroupBy(p => (p.LevelId, p.LevelLabel))
            .Select(g => new OccupancyLevelRow(
                g.Key.LevelId,
                g.Key.LevelLabel,
                g.Select(p => p.ServiceId).Distinct().Count(),
                g.Select(p => p.CellId).Distinct().Count(),
                g.Sum(p => p.Students),
                Peak(g),
                notAdmitting.GetValueOrDefault(g.Key.LevelId)))
            .OrderByDescending(l => l.PlacedStudents)
            .ToList();
    }

    /// <summary>
    /// The highest simultaneous load of a set of placements. ⚠ Never a sum: a cohort standing in a
    /// service in three separate windows is not three cohorts, and summing is what turns « 40
    /// students, three times » into « 120 students at once ».
    /// </summary>
    private static int Peak(IEnumerable<ReportPlacement> placements)
    {
        var segments = OccupancyTimeline.Build(placements.Select(ToPlacement));

        return segments.Count == 0 ? 0 : segments.Max(s => s.Occupants.Sum(o => o.Students));
    }

    // ── What the report says about its own blanks ──────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>An empty report has two causes that call for opposite acts</b> — no créneau authored
    /// (author an axis) or créneaux nobody is in (arrange) — and « 0 étudiant » collapses them into
    /// a third reading the user arrives at first: that the report is broken. Same shape as
    /// <c>RepartitionSummary.DeclaredSlotCount</c> separating those two, and the same rule as
    /// <c>ExportNotes</c>: silent when the data has nothing to say, because a warning that fires
    /// whatever the numbers are is noise, and noise is dismissed — which puts the real one out of
    /// sight.
    /// </summary>
    private static List<string> Notes(
        List<OccupancyServiceRow> rows, OccupancyReportTotals totals, List<Service> services)
    {
        var notes = new List<string>();

        if (totals.PlacementCount == 0)
        {
            notes.Add(
                "Aucune cellule de répartition n'est posée sur cette année : aucun groupe n'est "
                + "affecté à un service. Ce n'est pas une saturation nulle, c'est une planification "
                + "qui n'a pas commencé — posez l'axe depuis « Bloc de rotation », puis répartissez "
                + "depuis la grille d'un stage.");
        }

        if (services.Count > 1 && services.TrueForAll(s => !s.HasLevelRestrictions))
        {
            notes.Add(
                $"Aucun des {services.Count} services n'a de quota par promotion : chacun est ouvert "
                + "à toutes les promotions, qui se partagent son plafond total. Les dépassements "
                + "ci-dessous sont donc des dépassements d'effectif — ils se forcent à la publication "
                + "— et non des refus d'admission, qui eux ne se forcent pas.");
        }

        // ⚠ The number every saturation below is measured against, and on this base nobody wrote it:
        // all 148 services carry the value the import defaulted to. Same rule as StageCatalogueFigure
        // — say it when a figure is not one somebody authored, and stay silent otherwise.
        var openCapacities = services
            .Where(s => !s.HasLevelRestrictions)
            .Select(s => s.Capacity)
            .Distinct()
            .ToList();

        if (services.Count > 1 && openCapacities.Count == 1)
        {
            notes.Add(
                $"Les services déclarent tous la même capacité ({openCapacities[0]}). C'est la valeur "
                + "par défaut de l'import : les taux de saturation ci-dessous sont mesurés contre un "
                + "chiffre que personne n'a saisi.");
        }

        if (totals.ServicesNeverUsed > 0 && totals.PlacementCount > 0)
        {
            notes.Add(
                $"{totals.ServicesNeverUsed} service(s) n'accueillent personne de toute l'année. Vu "
                + "depuis la fiche d'un service cela ressemble à un service sans rien de prévu ; "
                + "c'est souvent l'autre moitié d'une saturation ailleurs.");
        }

        if (totals.ServicesAdmittingNobody > 0)
        {
            notes.Add(
                $"{totals.ServicesAdmittingNobody} service(s) accueillent une promotion que leurs "
                + "propres quotas n'admettent pas. Ce refus-là ne se force pas à la publication.");
        }

        return notes;
    }

    private static string Scope(List<Service> services, GetOccupancyReportQuery request, string yearLabel)
    {
        var parts = new List<string> { $"{services.Count} service(s)" };

        if (request.HospitalId is not null && services.Count > 0)
            parts.Add(services[0].Hospital.Name);

        if (request.LevelId is not null)
            parts.Add("une promotion");

        if (request.StageId is not null)
            parts.Add("un stage");

        if (request.OnlySaturated)
            parts.Add("services saturés uniquement");

        return $"{string.Join(" · ", parts)} — {yearLabel}";
    }

    private static OccupancyPlacement ToPlacement(ReportPlacement p) => new(
        p.StageId, p.StageName, p.LevelId, p.LevelLabel, p.PeriodNumber,
        p.CohortId, p.GroupNumber, p.Students, p.StartDate, p.EndDate);

    // ── The reads ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Named and <c>internal static</c> so <c>SqlTranslationTests</c> can hand it to
    /// <c>ToQueryString()</c> — a query buried in a private async method cannot be compiled without
    /// running it, and the in-memory provider translates nothing.
    /// </summary>
    internal static IQueryable<Service> ServicesQuery(IApplicationDbContext dbContext, int? hospitalId)
    {
        var query = dbContext.Services
            .AsNoTracking()
            .Include(s => s.Hospital)
            .Include(s => s.LevelCapacities)
                .ThenInclude(c => c.Level);

        return hospitalId is { } hospital
            ? query.Where(s => s.HospitalId == hospital).OrderBy(s => s.Name)
            : query.OrderBy(s => s.Name);
    }

    /// <summary>
    /// Every cell of the year, for every service at once. ⚠ Bounded by the year's <b>dates</b>, like
    /// the per-service read and like <c>SchedulePublisher</c>'s guard, so the report cannot disagree
    /// with the refusal it exists to explain.
    /// </summary>
    internal static IQueryable<ReportPlacement> PlacementsQuery(
        IApplicationDbContext dbContext, DateOnly yearStart, DateOnly yearEnd) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.StageSlot.StartDate <= yearEnd && yearStart <= a.StageSlot.EndDate)
            .Select(a => new ReportPlacement(
                a.Id,
                a.ServiceId,
                a.Service.Name,
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.Cohort.Stage.LevelId,
                a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                a.StageSlot.PeriodNumber,
                a.CohortId,
                a.Cohort.AcademicGroup.GroupNumber,
                // ⚠ The same count ServiceOccupancyCalculator and the publish guard use. A report
                // that measured the load differently from the guard would explain a refusal with a
                // number that never produced it.
                a.Cohort.Assignments.Count,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate));

    /// <summary>
    /// How many services each stage is <em>allowed</em> to use — the denominator of « il en utilise
    /// deux sur cinq », which is the finding no single service page can produce.
    /// </summary>
    internal static IQueryable<StageAllowedCount> AllowedServicesQuery(IApplicationDbContext dbContext) =>
        dbContext.Stages
            .AsNoTracking()
            .Select(s => new StageAllowedCount(s.Id, s.AllowedServices.Count));
}

/// <summary>One cell, flat by construction so the projection stays translatable.</summary>
internal sealed record ReportPlacement(
    int CellId,
    int ServiceId,
    string ServiceName,
    int StageId,
    string StageName,
    int LevelId,
    string LevelLabel,
    int PeriodNumber,
    int CohortId,
    int GroupNumber,
    int Students,
    DateOnly StartDate,
    DateOnly EndDate);

internal sealed record StageAllowedCount(int StageId, int AllowedServices);
