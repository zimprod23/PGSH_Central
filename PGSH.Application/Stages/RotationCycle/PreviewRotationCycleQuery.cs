using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// What the crossover would look like. Writes nothing, and the layout it returns is the one the apply
/// executes — same planner, so the dry run is the plan.
/// </summary>
/// <param name="StageIds">
/// The stages that run <em>concurrently</em> on one shared axis. The new CNPN's 3rd year is two such
/// blocks — three stages in the first semester, three in the second — not one block of six.
/// </param>
/// <param name="Windows">
/// The block's columns, in order. Supplied rather than computed: an academic calendar has holidays and
/// irregular boundaries, and inventing dates from a start and a length would get them wrong.
/// </param>
public sealed record PreviewRotationCycleQuery(
    int LevelId,
    IReadOnlyList<int> StageIds,
    int PeriodsPerStage,
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
    bool CanApply);

public sealed record RotationCycleStage(int StageId, string Name);

internal sealed class PreviewRotationCycleQueryValidator : AbstractValidator<PreviewRotationCycleQuery>
{
    public PreviewRotationCycleQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.StageIds).NotEmpty();
        RuleFor(x => x.PeriodsPerStage).GreaterThan(0);
        RuleFor(x => x.Windows).NotEmpty();
    }
}

internal sealed class PreviewRotationCycleQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    RotationCycleContext context)
    : IQueryHandler<PreviewRotationCycleQuery, RotationCyclePreview>
{
    public async Task<Result<RotationCyclePreview>> Handle(
        PreviewRotationCycleQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<RotationCyclePreview>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var resolved = await context.ResolveAsync(request.LevelId, request.StageIds, yearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<RotationCyclePreview>(resolved.Error);

        var layout = RotationCyclePlanner.Build(
            request.StageIds,
            request.PeriodsPerStage,
            resolved.Value.PartitionLabels,
            request.Windows.Select(w => (w.StartDate, w.EndDate)).ToList());

        if (layout.IsFailure)
            return Result.Failure<RotationCyclePreview>(layout.Error);

        return new RotationCyclePreview(
            yearLabel,
            resolved.Value.LevelLabel,
            resolved.Value.Stages,
            layout.Value,
            resolved.Value.ExistingSlots,
            resolved.Value.PublishedCells,
            CanApply: resolved.Value.PublishedCells == 0);
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
            .Select(s => new { s.Id, s.Name, s.LevelId })
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
            .Where(g => g.AcademicYearId == academicYearId
                     && (g.LevelId == levelId || g.Registrations.Any(r => r.LevelId == levelId)))
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
                  .Select(s => new RotationCycleStage(s.Id, s.Name))
                  .ToList(),
            partitionLabels.OrderBy(l => l).ToList(),
            existingSlots,
            publishedCells);
    }
}
