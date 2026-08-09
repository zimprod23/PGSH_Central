using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Timeline;

/// <summary>
/// Builds the Year → Level → Stage → Partition timeline tree for the calendar view.
/// A <c>Stage</c> has no dates of its own — every span here is derived from
/// <c>StageSlot</c> windows: a stage spans the union of its slots, a partition spans
/// the slots its cohorts actually occupy. Slots are year-stamped, cohorts reach the year
/// through <c>AcademicGroup.AcademicYearId</c>; both are filtered, so a stage that ran for
/// several promotions spans only the requested year's windows.
/// </summary>
internal sealed class GetYearTimelineQueryHandler(
    IApplicationDbContext dbContext,
    ServiceOccupancyCalculator occupancyCalculator,
    ServiceIntakeCalculator intakeCalculator)
    : IQueryHandler<GetYearTimelineQuery, YearTimelineResponse>
{
    public async Task<Result<YearTimelineResponse>> Handle(
        GetYearTimelineQuery request, CancellationToken ct)
    {
        var year = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == request.AcademicYearId)
            .Select(y => new { y.Id, y.Label })
            .FirstOrDefaultAsync(ct);

        if (year is null)
            return Result.Failure<YearTimelineResponse>(
                Error.NotFound("AcademicYears.NotFound", $"The academic year with Id = '{request.AcademicYearId}' was not found."));

        var cohorts = (await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroup.AcademicYearId == request.AcademicYearId)
            .Where(c => request.LevelId == null || c.Stage.LevelId == request.LevelId)
            .Where(c => request.StageId == null || c.StageId == request.StageId)
            .Select(c => new
            {
                c.Id,
                c.StageId,
                StageName = c.Stage.Name,
                c.Stage.LevelId,
                LevelLabel = c.Stage.Level.Label,
                Partition = c.AcademicGroup.RotationGroup,
                c.AcademicGroupId,
                GroupLabel = c.AcademicGroup.Label,
                GroupNumber = c.AcademicGroup.GroupNumber,
                StudentCount = c.Assignments.Count,
                Cells = c.SlotAssignments.Select(a => new CellRow(
                    a.ServiceId,
                    a.StageSlot.StartDate,
                    a.StageSlot.EndDate)).ToList(),
            })
            .ToListAsync(ct))
            .Select(c => new CohortRow(
                c.Id, c.StageId, c.StageName, c.LevelId, c.LevelLabel,
                c.Partition, c.AcademicGroupId, c.GroupLabel, c.GroupNumber, c.StudentCount, c.Cells))
            .ToList();

        if (cohorts.Count == 0)
            return new YearTimelineResponse(year.Id, year.Label, null, null, []);

        // Actual (possibly pause-shifted) period windows + their pause bands, keyed by the cohort
        // the student currently sits in. A paused rotation extends past its planned slot window, so
        // the calendar reflects the real end and draws the suspension as a band.
        var cohortIds = cohorts.Select(c => c.CohortId).ToList();
        var periodsByCohort = (await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => cohortIds.Contains(p.InternshipAssignment.CurrentCohortId) && !p.IsInterrupted)
            .Select(p => new
            {
                CohortId = p.InternshipAssignment.CurrentCohortId,
                p.EndDate,
                Pauses = p.Pauses.Select(x => new PauseRow(x.StartDate, x.ResumeDate, x.Kind.ToString())).ToList(),
            })
            .ToListAsync(ct))
            .GroupBy(x => x.CohortId)
            .ToDictionary(
                g => g.Key,
                g => new CohortPeriods(
                    g.Max(x => (DateOnly?)x.EndDate),
                    g.SelectMany(x => x.Pauses).ToList()));

        var stageIds = cohorts.Select(c => c.StageId).Distinct().ToList();

        var stageSpans = (await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => stageIds.Contains(s.StageId) && s.AcademicYearId == request.AcademicYearId)
            .Select(s => new { s.StageId, s.StartDate, s.EndDate })
            .ToListAsync(ct))
            .GroupBy(s => s.StageId)
            .ToDictionary(g => g.Key, g => (Start: g.Min(x => x.StartDate), End: g.Max(x => x.EndDate)));

        var slotCountByStage = stageSpans.Count > 0
            ? await dbContext.StageSlots
                .AsNoTracking()
                .Where(s => stageIds.Contains(s.StageId) && s.AcademicYearId == request.AcademicYearId)
                .GroupBy(s => s.StageId)
                .Select(g => new { StageId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StageId, x => x.Count, ct)
            : [];

        var serviceIds = cohorts.SelectMany(c => c.Cells.Select(cell => cell.ServiceId)).Distinct().ToList();
        var occupancy = await occupancyCalculator.BuildAsync(serviceIds, ct);
        var intake = await intakeCalculator.BuildAsync(serviceIds, ct);

        var levels = cohorts
            .GroupBy(c => new { c.LevelId, c.LevelLabel })
            .OrderBy(lg => lg.Key.LevelLabel)
            .Select(lg =>
            {
                var stages = lg
                    .GroupBy(c => new { c.StageId, c.StageName })
                    .OrderBy(sg => sg.Key.StageName)
                    .Select(sg => BuildStage(sg.Key.StageId, sg.Key.StageName, lg.Key.LevelId, sg.ToList(), stageSpans, slotCountByStage, occupancy, intake, periodsByCohort))
                    .ToList();

                var (start, end) = SpanOf(stages.Select(s => (s.Start, s.End)));
                return new TimelineLevel(lg.Key.LevelId, lg.Key.LevelLabel, start, end, stages);
            })
            .ToList();

        var (yearStart, yearEnd) = SpanOf(levels.Select(l => (l.Start, l.End)));
        return new YearTimelineResponse(year.Id, year.Label, yearStart, yearEnd, levels);
    }

    private static TimelineStage BuildStage(
        int stageId, string stageName, int levelId, List<CohortRow> stageCohorts,
        IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> stageSpans,
        IReadOnlyDictionary<int, int> slotCountByStage,
        ServiceOccupancyLookup occupancy,
        ServiceIntakeLookup intake,
        IReadOnlyDictionary<int, CohortPeriods> periodsByCohort)
    {
        var partitions = stageCohorts
            .GroupBy(c => c.Partition)
            .OrderBy(pg => pg.Key ?? "~")
            .Select(pg =>
            {
                var cells = pg.SelectMany(c => c.Cells).ToList();
                DateOnly? start = cells.Count > 0 ? cells.Min(x => x.Start) : null;
                DateOnly? end   = cells.Count > 0 ? cells.Max(x => x.End)   : null;

                var partitionPeriods = pg
                    .Select(c => periodsByCohort.GetValueOrDefault(c.CohortId))
                    .Where(x => x is not null)
                    .Select(x => x!)
                    .ToList();

                // A paused rotation runs past its planned slot window — extend the bar to the real end.
                var periodEnds = partitionPeriods.Where(x => x.MaxEnd.HasValue).Select(x => x.MaxEnd!.Value).ToList();
                if (periodEnds.Count > 0)
                {
                    var maxPeriodEnd = periodEnds.Max();
                    end = end is null || maxPeriodEnd > end ? maxPeriodEnd : end;
                }

                var pauses = partitionPeriods
                    .SelectMany(x => x.Pauses)
                    .Select(p => new TimelinePauseBand(p.Start, p.Resume, p.Kind))
                    .GroupBy(b => new { b.Start, b.End, b.Kind })
                    .Select(g => g.First())
                    .OrderBy(b => b.Start)
                    .ToList();

                // One ceiling each, matching publish: a restricted service is measured on this
                // promotion alone against its quota, an unrestricted one on everybody against its total.
                bool saturated = cells.Any(cell =>
                    !intake.Admits(cell.ServiceId, levelId)
                    || (intake.HasLevelRestrictions(cell.ServiceId)
                            ? occupancy.LoadOn(cell.ServiceId, levelId, cell.Start, cell.End)
                            : occupancy.LoadOn(cell.ServiceId, cell.Start, cell.End))
                        > intake.CapacityFor(cell.ServiceId, levelId));

                var groups = pg
                    .GroupBy(c => new { c.AcademicGroupId, c.GroupLabel, c.GroupNumber })
                    .OrderBy(gg => gg.Key.GroupNumber)
                    .Select(gg => new TimelineGroup(
                        gg.Key.AcademicGroupId, gg.Key.GroupLabel, gg.Key.GroupNumber, gg.Sum(c => c.StudentCount)))
                    .ToList();

                return new TimelinePartition(
                    pg.Key, start, end, pg.Count(), pg.Sum(c => c.StudentCount), saturated, groups, pauses);
            })
            .ToList();

        DateOnly? stageStart = null, stageEnd = null;
        if (stageSpans.TryGetValue(stageId, out var span))
            (stageStart, stageEnd) = (span.Start, span.End);
        else
            (stageStart, stageEnd) = SpanOf(partitions.Select(p => (p.Start, p.End)));

        return new TimelineStage(
            stageId,
            stageName,
            stageStart,
            stageEnd,
            slotCountByStage.GetValueOrDefault(stageId),
            stageCohorts.Count,
            partitions.Count,
            partitions.Any(p => p.Saturated),
            partitions);
    }

    private static (DateOnly? Start, DateOnly? End) SpanOf(IEnumerable<(DateOnly? Start, DateOnly? End)> ranges)
    {
        var starts = ranges.Where(r => r.Start.HasValue).Select(r => r.Start!.Value).ToList();
        var ends = ranges.Where(r => r.End.HasValue).Select(r => r.End!.Value).ToList();
        return (starts.Count > 0 ? starts.Min() : null, ends.Count > 0 ? ends.Max() : null);
    }

    private sealed record CohortRow(
        int CohortId, int StageId, string StageName, int LevelId, string? LevelLabel,
        string? Partition, int AcademicGroupId, string GroupLabel, int GroupNumber,
        int StudentCount, List<CellRow> Cells);

    private sealed record CellRow(int ServiceId, DateOnly Start, DateOnly End);

    private sealed record CohortPeriods(DateOnly? MaxEnd, List<PauseRow> Pauses);

    private sealed record PauseRow(DateOnly Start, DateOnly? Resume, string Kind);
}
