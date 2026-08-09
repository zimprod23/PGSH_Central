using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.MacroPlan;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Authors the block's shared axis: every stage of the block gets a slot on every column, from one
/// list of windows. That is the point — the columns line up because they are the <em>same</em> dates
/// written once, not because everyone typed them identically.
/// </summary>
/// <remarks>
/// <para>It stops there, and returns the matrix rather than executing it. Authoring the axis and
/// running the plan are two different acts — the same separation as déliberation and réinscription —
/// and the second one already exists: hand <see cref="RotationCycleResult.Matrix"/> to
/// <c>GenerateMacroPlanCommand</c>, which provisions cohorts, affects students, arranges services and
/// publishes exactly as it does for a hand-ticked matrix. Nothing downstream of the matrix changes.</para>
/// </remarks>
public sealed record ApplyRotationCycleCommand(
    int LevelId,
    IReadOnlyList<int> StageIds,
    int PeriodsPerStage,
    IReadOnlyList<DateWindow> Windows,
    int? AcademicYearId = null) : ICommand<RotationCycleResult>, IAuditableCommand
{
    public string AuditAction => "ROTATION_CYCLE_APPLIED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        levelId = LevelId,
        academicYearId = AcademicYearId,
        stageIds = StageIds,
        periodsPerStage = PeriodsPerStage,
        columns = Windows.Count,
    });
}

public sealed record RotationCycleResult(
    int SlotsCreated,
    int SlotsReplaced,
    RotationCycleLayout Layout,
    // Feed this to GenerateMacroPlanCommand to actually place the cohorts.
    IReadOnlyList<PartitionStagePlan> Matrix);

internal sealed class ApplyRotationCycleCommandValidator : AbstractValidator<ApplyRotationCycleCommand>
{
    public ApplyRotationCycleCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.StageIds).NotEmpty();
        RuleFor(x => x.PeriodsPerStage).GreaterThan(0);
        RuleFor(x => x.Windows).NotEmpty();
        RuleForEach(x => x.Windows)
            .Must(w => w.EndDate >= w.StartDate)
            .WithMessage("Une fenêtre se termine avant de commencer.");
    }
}

internal sealed class ApplyRotationCycleCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    RotationCycleContext context)
    : ICommandHandler<ApplyRotationCycleCommand, RotationCycleResult>
{
    public async Task<Result<RotationCycleResult>> Handle(
        ApplyRotationCycleCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<RotationCycleResult>(year.Error);

        int yearId = year.Value;

        var resolved = await context.ResolveAsync(request.LevelId, request.StageIds, yearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<RotationCycleResult>(resolved.Error);

        // A cell backed by a ServicePeriod is an execution record. Rewriting the axis under it would
        // leave the published plan describing windows that no longer exist — the same refusal, for the
        // same reason, as re-cutting partitions under a published plan.
        if (resolved.Value.PublishedCells > 0)
            return Result.Failure<RotationCycleResult>(
                RotationCycleErrors.CannotReplacePublished(resolved.Value.PublishedCells));

        var layout = RotationCyclePlanner.Build(
            request.StageIds,
            request.PeriodsPerStage,
            resolved.Value.PartitionLabels,
            request.Windows.Select(w => (w.StartDate, w.EndDate)).ToList());

        if (layout.IsFailure)
            return Result.Failure<RotationCycleResult>(layout.Error);

        // Replacing rather than merging: a block's axis is one object, and half-old half-new columns
        // are exactly the misalignment this feature exists to remove. Cells hanging off the old slots
        // go with them — unpublished, so an arrange rebuilds them from the returned matrix.
        var obsolete = await dbContext.StageSlots
            .Where(s => request.StageIds.Contains(s.StageId) && s.AcademicYearId == yearId)
            .ToListAsync(cancellationToken);

        if (obsolete.Count > 0)
            dbContext.StageSlots.RemoveRange(obsolete);

        var created = new List<StageSlot>(request.StageIds.Count * layout.Value.Columns.Count);

        foreach (int stageId in request.StageIds)
        {
            foreach (var column in layout.Value.Columns)
            {
                created.Add(new StageSlot
                {
                    StageId = stageId,
                    AcademicYearId = yearId,
                    PeriodNumber = column.PeriodNumber,
                    Label = $"P{column.PeriodNumber}",
                    StartDate = column.StartDate,
                    EndDate = column.EndDate,
                });
            }
        }

        await dbContext.StageSlots.AddRangeAsync(created, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RotationCycleResult(
            created.Count, obsolete.Count, layout.Value, layout.Value.Matrix);
    }
}
