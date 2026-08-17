using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Repartition;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Occupancy;

internal sealed class GetServiceOccupancyQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetServiceOccupancyQuery, ServiceOccupancyResponse>
{
    public async Task<Result<ServiceOccupancyResponse>> Handle(
        GetServiceOccupancyQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<ServiceOccupancyResponse>(year.Error);

        var window = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == year.Value)
            .Select(y => new { y.Label, y.StartDate, y.EndDate })
            .FirstAsync(cancellationToken);

        var service = await dbContext.Services
            .AsNoTracking()
            .Include(s => s.LevelCapacities)
                .ThenInclude(c => c.Level)
            .Include(s => s.Hospital)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure<ServiceOccupancyResponse>(ServiceErrors.NotFound(request.ServiceId));

        // Bounded by the year's dates, not by AcademicYearId — see the query's remarks. Two academic
        // years never overlap on the calendar, so this loses nothing, and it cannot disagree with the
        // publish guard, which filters by date alone.
        var placements = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.ServiceId == request.ServiceId
                     && a.StageSlot.StartDate <= window.EndDate
                     && window.StartDate <= a.StageSlot.EndDate)
            .Select(a => new OccupancyPlacement(
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.Cohort.Stage.LevelId,
                a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                a.StageSlot.PeriodNumber,
                a.CohortId,
                a.Cohort.AcademicGroup.GroupNumber,
                // ⚠ The same count ServiceOccupancyCalculator uses. A page that measured the load
                // differently from the guard would explain a refusal with a number that never
                // produced it.
                a.Cohort.Assignments.Count,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate))
            .ToListAsync(cancellationToken);

        var rule = service.HasLevelRestrictions ? CapacityRule.PerLevel : CapacityRule.Total;

        var segments = OccupancyTimeline.Build(placements)
            .Select(segment => Describe(segment, service, rule))
            .ToList();

        var peak = segments.OrderByDescending(s => s.Students).FirstOrDefault();

        var summary = new OccupancySummaryResponse(
            SegmentCount:        segments.Count,
            OverCapacitySegments: segments.Count(s => s.Overflow > 0),
            PeakStudents:        peak?.Students ?? 0,
            PeakStart:           peak?.StartDate,
            PeakEnd:             peak?.EndDate,
            DistinctStages:      placements.Select(p => p.StageId).Distinct().Count(),
            DistinctLevels:      placements.Select(p => p.LevelId).Distinct().Count(),
            DaysOverCapacity:    segments.Where(s => s.Overflow > 0).Sum(s => s.Days));

        return Result.Success(new ServiceOccupancyResponse(
            service.Id,
            service.Name,
            service.Hospital.Name,
            year.Value,
            window.Label,
            rule,
            service.Capacity,
            service.LevelCapacities
                .OrderBy(c => c.Level.AcademicProgram)
                .ThenBy(c => c.Level.Year)
                .Select(c => new LevelQuotaResponse(
                    c.LevelId, c.Level.Label ?? $"niveau {c.LevelId}", c.Capacity))
                .ToList(),
            segments,
            summary));
    }

    /// <summary>
    /// One segment measured against whichever ceiling is actually in force — the same branch
    /// <c>SchedulePublisher.EnsureCapacityAsync</c> takes, so the page and the refusal agree:
    /// a restricted service is measured per promotion against that promotion's quota, an
    /// unrestricted one on everybody at once against its total.
    /// </summary>
    private static OccupancySegmentResponse Describe(
        OccupancySegment segment, Service service, CapacityRule rule)
    {
        int students = segment.Occupants.Sum(o => o.Students);

        var levels = segment.Occupants
            .GroupBy(o => (o.LevelId, o.LevelLabel))
            .Select(g =>
            {
                int load = g.Sum(o => o.Students);

                if (rule == CapacityRule.Total)
                    return new SegmentLevelLoadResponse(
                        g.Key.LevelId, g.Key.LevelLabel, load, null, 0, NotAdmitted: false);

                int quota = service.CapacityFor(g.Key.LevelId);
                return new SegmentLevelLoadResponse(
                    g.Key.LevelId,
                    g.Key.LevelLabel,
                    load,
                    quota,
                    Math.Max(0, load - quota),
                    NotAdmitted: !service.Admits(g.Key.LevelId));
            })
            .OrderByDescending(l => l.Students)
            .ToList();

        int overflow = rule == CapacityRule.Total
            ? Math.Max(0, students - service.Capacity)
            : levels.Sum(l => l.Overflow);

        var occupants = segment.Occupants
            .GroupBy(o => (o.StageId, o.StageName, o.LevelId, o.LevelLabel, o.PeriodNumber))
            .Select(g => new SegmentOccupantResponse(
                g.Key.StageId,
                g.Key.StageName,
                g.Key.LevelId,
                g.Key.LevelLabel,
                g.Key.PeriodNumber,
                GroupNumberRanges.Format(g.Select(o => o.GroupNumber)),
                g.Select(o => o.CohortId).Distinct().Count(),
                g.Sum(o => o.Students)))
            .OrderByDescending(o => o.Students)
            .ThenBy(o => o.StageName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OccupancySegmentResponse(
            segment.StartDate,
            segment.EndDate,
            segment.EndDate.DayNumber - segment.StartDate.DayNumber + 1,
            students,
            rule == CapacityRule.Total ? service.Capacity : null,
            overflow,
            levels,
            occupants);
    }
}
