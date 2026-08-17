using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// What the crossover would look like. Writes nothing, and the layout it returns is the one the apply
/// executes — same planner, so the dry run is the plan.
/// </summary>
/// <param name="Stages">
/// The stages that run <em>concurrently</em> on one shared axis, each with how many columns a partition
/// spends in it. They need not agree: the 6th year is four stages of two periods and two of one. The new
/// CNPN's 3rd year is instead two blocks of three — a semester each — not one block of six.
/// </param>
/// <param name="Windows">
/// The block's columns, in order, at the <em>finest</em> granularity any of its stages uses — entered
/// once for the whole block. Each stage's own slots are then whole runs of these, so a two-period stage
/// on a monthly axis gets five two-month slots and a one-period stage gets ten. Supplied rather than
/// computed: an academic calendar has holidays and irregular boundaries.
/// </param>
public sealed record PreviewRotationCycleQuery(
    int LevelId,
    IReadOnlyList<RotationStage> Stages,
    IReadOnlyList<DateWindow> Windows,
    int? AcademicYearId = null) : IQuery<RotationCyclePreview>;

public sealed record DateWindow(DateOnly StartDate, DateOnly EndDate);

public sealed record RotationCyclePreview(
    string AcademicYearLabel,
    string LevelLabel,
    IReadOnlyList<RotationCycleStage> Stages,
    RotationCycleLayout Layout,
    // Slots these stages already hold for the year, which applying would replace.
    int ExistingSlots,
    int PublishedCells,
    bool CanApply,
    // What the windows actually give each stage, in jours ouvrables, against what it says it needs.
    IReadOnlyList<StageDurationCheck> DurationChecks,
    // No holiday recorded across the axis at all, so the counts below are calendar days minus weekends.
    bool CalendarIsEmpty);

public sealed record RotationCycleStage(int StageId, string Name, int DurationInDays);

/// <summary>
/// What one stage of the block actually gets, measured on the calendar, against the duration its catalogue
/// row states.
///
/// <para>Reported per stage rather than per partition, as a range: partitions take <em>different</em> runs
/// of the axis, and a run over février is genuinely shorter than one over mars. The spread is a fact about
/// calendars, not a defect — which is why this is a report and not a guard.</para>
/// </summary>
/// <param name="StatedDurationInDays">
/// ⚠ <c>Stage.DurationInDays</c>, the catalogue's own number — which duplicates the one every
/// <c>CurriculumStage</c> carries and is not necessarily what any CNPN states. They agree today only
/// because the history reconstruction seeded one from the other. See <c>PHASES.md</c> 15.1.
/// </param>
/// <param name="Note">
/// Set only when the gap is worth a human look, and never blocking — the faculty decides. An axis
/// authored in worked days typically leaves every note null, since the stored durations are themselves
/// in worked days; a mismatch means the axis and the catalogue genuinely disagree.
/// </param>
public sealed record StageDurationCheck(
    int StageId,
    string Name,
    int Periods,
    int StatedDurationInDays,
    int MinWorkingDays,
    int MaxWorkingDays,
    int MinCalendarDays,
    int MaxCalendarDays,
    string? Note);

internal sealed class PreviewRotationCycleQueryValidator : AbstractValidator<PreviewRotationCycleQuery>
{
    public PreviewRotationCycleQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Stages).NotEmpty();
        RuleForEach(x => x.Stages).Must(st => st.Periods >= 1)
            .WithMessage("Chaque stage occupe au moins une période.");
        RuleFor(x => x.Windows).NotEmpty();
    }
}

internal sealed class PreviewRotationCycleQueryHandler(
    AcademicYearResolver yearResolver,
    RotationCycleContext context,
    WorkingDayProvider workingDays)
    : IQueryHandler<PreviewRotationCycleQuery, RotationCyclePreview>
{
    public async Task<Result<RotationCyclePreview>> Handle(
        PreviewRotationCycleQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<RotationCyclePreview>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var stageIds = request.Stages.Select(st => st.StageId).ToList();
        var resolved = await context.ResolveAsync(request.LevelId, stageIds, yearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<RotationCyclePreview>(resolved.Error);

        var layout = RotationCyclePlanner.Build(
            request.Stages,
            resolved.Value.PartitionLabels,
            request.Windows.Select(w => (w.StartDate, w.EndDate)).ToList());

        if (layout.IsFailure)
            return Result.Failure<RotationCyclePreview>(layout.Error);

        var calendar = await workingDays.BuildAsync(cancellationToken);

        var span = (
            From: request.Windows.Min(w => w.StartDate),
            To: request.Windows.Max(w => w.EndDate));

        return new RotationCyclePreview(
            yearLabel,
            resolved.Value.LevelLabel,
            resolved.Value.Stages,
            layout.Value,
            resolved.Value.ExistingSlots,
            resolved.Value.PublishedCells,
            CanApply: resolved.Value.PublishedCells == 0,
            DurationChecks: Check(request.Stages, resolved.Value.Stages, layout.Value, calendar),
            CalendarIsEmpty: calendar.HolidaysBetween(span.From, span.To).Count == 0);
    }

    /// <summary>
    /// Measures each stage's placements on the calendar. A partition's time in a stage is the run of slots
    /// carrying the period numbers the matrix gave it, so its span is those slots' first start to last end —
    /// read off the layout rather than recomputed, which is what keeps this agreeing with what gets written.
    /// </summary>
    private static List<StageDurationCheck> Check(
        IReadOnlyList<RotationStage> requested,
        IReadOnlyList<RotationCycleStage> stages,
        RotationCycleLayout layout,
        WorkingDayCalendar calendar)
    {
        var checks = new List<StageDurationCheck>(stages.Count);

        foreach (var stage in stages)
        {
            int periods = requested.First(r => r.StageId == stage.StageId).Periods;
            var slots = layout.Slots.Where(s => s.StageId == stage.StageId).ToList();

            var spans = layout.Matrix
                .Where(m => m.StageId == stage.StageId)
                .Select(m => slots.Where(s => m.PeriodNumbers.Contains(s.PeriodNumber)).ToList())
                .Where(run => run.Count > 0)
                .Select(run => (
                    Working: calendar.Count(run.Min(s => s.StartDate), run.Max(s => s.EndDate)),
                    Calendar: run.Max(s => s.EndDate).DayNumber - run.Min(s => s.StartDate).DayNumber + 1))
                .ToList();

            if (spans.Count == 0)
                continue;

            int minWorking = spans.Min(s => s.Working);
            int maxWorking = spans.Max(s => s.Working);

            checks.Add(new StageDurationCheck(
                stage.StageId,
                stage.Name,
                periods,
                stage.DurationInDays,
                minWorking,
                maxWorking,
                spans.Min(s => s.Calendar),
                spans.Max(s => s.Calendar),
                Note(stage, minWorking, maxWorking, spans.Min(s => s.Calendar))));
        }

        return checks;
    }

    private static string? Note(RotationCycleStage stage, int minWorking, int maxWorking, int minCalendar)
    {
        // A stated 30 that the placement meets in calendar days but not in worked days is the ambiguity in
        // the column itself, not a badly cut axis — say which reading was met rather than just "short".
        if (minWorking < stage.DurationInDays && minCalendar >= stage.DurationInDays)
            return $"{stage.DurationInDays} jours annoncés : atteints en jours calendaires "
                 + $"({minCalendar}), pas en jours ouvrables ({minWorking}).";

        if (maxWorking < stage.DurationInDays)
            return $"{maxWorking} jours ouvrables au mieux pour {stage.DurationInDays} annoncés.";

        if (maxWorking - minWorking >= 5)
            return $"De {minWorking} à {maxWorking} jours ouvrables selon la partition — "
                 + $"{maxWorking - minWorking} jours d'écart.";

        return null;
    }
}

/// <summary>
/// The database half both the preview and the apply need: the level, the stages, the promotion's
/// partition labels, and what is already planned on those stages. Shared so the two cannot disagree
/// about what they are looking at.
/// </summary>
internal sealed class RotationCycleContext(IApplicationDbContext dbContext)
{
    internal sealed record Resolution(
        string LevelLabel,
        IReadOnlyList<RotationCycleStage> Stages,
        IReadOnlyList<string> PartitionLabels,
        int ExistingSlots,
        int PublishedCells);

    public async Task<Result<Resolution>> ResolveAsync(
        int levelId, IReadOnlyList<int> stageIds, int academicYearId, CancellationToken ct)
    {
        // Checked here and not only in the planner: this method runs first and indexes the stage ids
        // into a dictionary, which throws on a duplicate key — so the planner's own DuplicateStage
        // guard was unreachable through the handler, and a repeated id came back as a 500.
        if (stageIds.Distinct().Count() != stageIds.Count)
            return Result.Failure<Resolution>(RotationCycleErrors.DuplicateStage);

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => new { l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (level is null)
            return Result.Failure<Resolution>(RegistrationErrors.MissingLevel);

        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(s => stageIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.LevelId, s.DurationInDays })
            .ToListAsync(ct);

        foreach (int stageId in stageIds)
        {
            var stage = stages.FirstOrDefault(s => s.Id == stageId);
            if (stage is null)
                return Result.Failure<Resolution>(StageErrors.NotFound(stageId));

            // A block is a level's timetable. Letting another level's stage in would put its cohorts on
            // windows this level's overlap rules never checked.
            if (stage.LevelId != levelId)
                return Result.Failure<Resolution>(
                    RotationCycleErrors.StageNotOfLevel(stageId, levelLabel));
        }

        var partitionLabels = await dbContext.AcademicGroups
            .AsNoTracking()
            // LevelId alone — a roster with no promotion is « Non réparti », not a partition of this
            // block. See AssignRotationGroupsCommandHandler for why the registration fallback went.
            .Where(g => g.AcademicYearId == academicYearId && g.LevelId == levelId)
            .Where(g => g.RotationGroup != null)
            .Select(g => g.RotationGroup!)
            .Distinct()
            .ToListAsync(ct);

        int existingSlots = await dbContext.StageSlots
            .CountAsync(s => stageIds.Contains(s.StageId) && s.AcademicYearId == academicYearId, ct);

        int publishedCells = await dbContext.ServicePeriods
            .CountAsync(p => p.CohortSlotAssignmentId != null
                          && stageIds.Contains(p.CohortSlotAssignment!.StageSlot.StageId)
                          && p.CohortSlotAssignment.StageSlot.AcademicYearId == academicYearId, ct);

        // Kept in the order the caller listed them: that order *is* the rotation, so reordering here
        // would silently change which stage a partition starts in.
        var order = stageIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        return new Resolution(
            levelLabel,
            stages.OrderBy(s => order[s.Id])
                  .Select(s => new RotationCycleStage(s.Id, s.Name, s.DurationInDays))
                  .ToList(),
            partitionLabels.OrderBy(l => l).ToList(),
            existingSlots,
            publishedCells);
    }
}
