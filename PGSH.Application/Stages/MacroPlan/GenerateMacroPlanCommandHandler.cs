using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.MacroPlan;

internal sealed class GenerateMacroPlanCommandHandler(
    IApplicationDbContext dbContext,
    CohortProvisioner provisioner,
    StudentAffectationService affectation,
    RotationArranger arranger,
    SchedulePublisher publisher)
    : ICommandHandler<GenerateMacroPlanCommand, MacroPlanResult>
{
    /// <summary>
    /// ⚠ <b>One transaction over the whole matrix.</b> Every step below saves for itself — cohorts,
    /// then affectations and cells stage by stage — so on a promotion of a hundred rosters this
    /// handler used to commit a dozen times before it was finished. Closing the tab or losing the
    /// connection cancels the request between two of those commits, and what stayed behind was a
    /// plan built for the first three stages and nothing for the rest: not obviously broken, simply
    /// wrong, and indistinguishable from a plan somebody meant that way.
    /// </summary>
    public Task<Result<MacroPlanResult>> Handle(
        GenerateMacroPlanCommand request, CancellationToken cancellationToken) =>
        dbContext.ExecuteAtomicallyAsync(ct => PlanAsync(request, ct), cancellationToken);

    private async Task<Result<MacroPlanResult>> PlanAsync(
        GenerateMacroPlanCommand request, CancellationToken cancellationToken)
    {
        var cohortResult = await provisioner.EnsureCohortsAsync(
            request.AcademicYearId,
            request.Plans.Select(p => (p.RotationGroup, p.StageId)).ToList(),
            cancellationToken);

        if (cohortResult.IsFailure)
            return Result.Failure<MacroPlanResult>(cohortResult.Error);

        var blocks = ConcurrencyBlock.From(request.Plans);

        int studentsAssigned = 0, cellsArranged = 0, saturated = 0, cohortsPublished = 0, periodsPublished = 0;
        int groupConflicts = 0, skippedAlreadyServed = 0;

        foreach (var block in blocks)
        {
            if (request.AssignStudents)
            {
                var affected = await affectation.AssignByStageAsync(
                    block.StageId, request.AcademicYearId, block.RotationGroups, cancellationToken);
                studentsAssigned += affected.SuccessCount;
            }

            if (request.AutoArrange)
            {
                var arranged = await arranger.ArrangeAsync(
                    block.StageId, request.AcademicYearId, block.RotationGroups, block.PeriodNumbers,
                    null, cancellationToken);

                // A stage whose period slots aren't defined yet is a setup-order issue,
                // not a hard error: keep the cohorts/affectation already done and let the
                // admin define slots then re-run. Other failures still surface.
                if (arranged.IsFailure)
                {
                    if (arranged.Error.Code == "Schedule.NoSlots") continue;
                    return Result.Failure<MacroPlanResult>(arranged.Error);
                }

                cellsArranged  += arranged.Value.Assigned;
                saturated      += arranged.Value.SaturatedServices;
                // Same reason the auto-arrange path reports it: a partition authored to collide
                // with another stage's window arranges nothing, and "0 cells" alone reads as
                // "there was nothing to do".
                groupConflicts += arranged.Value.GroupConflicts;
            }
        }

        if (request.Publish)
        {
            foreach (var block in blocks)
            {
                var published = await publisher.PublishStageAsync(
                    block.StageId, request.AcademicYearId, block.RotationGroups, block.PeriodNumbers,
                    request.AllowOverCapacity, cancellationToken);
                if (published.IsFailure)
                    return Result.Failure<MacroPlanResult>(published.Error);

                cohortsPublished     += published.Value.PublishedCohorts;
                periodsPublished     += published.Value.PeriodsCreated;
                skippedAlreadyServed += published.Value.SkippedAlreadyServed;
            }
        }

        return Result.Success(new MacroPlanResult(
            cohortResult.Value.Created,
            cohortResult.Value.Skipped,
            studentsAssigned,
            cellsArranged,
            saturated,
            cohortsPublished,
            periodsPublished,
            cohortResult.Value.NotRequiredByCnpn,
            groupConflicts,
            skippedAlreadyServed));
    }
}

/// <summary>
/// The partitions that occupy one stage over one window — everything the matrix puts in the same
/// place at the same time. <c>Lₛ = P·kₛ/T</c> of them, which is more than one exactly when the
/// block's stages have unequal durations.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Concurrent partitions must be arranged in one call, not one each.</b> The service
/// queue is balanced over the cohorts of a single call, so arranging them separately balances each
/// partition against the full service list in ignorance of the others — and the leftovers stack,
/// because <c>BuildServiceQueue</c>'s stable ordering always hands the remainder to the same leading
/// services and every partition of a column carries the same rotation offset. Measured on the
/// 5th-year block (Gynécologie k=3, L=3, five services, 20 groups): three separate calls of 7/7/6
/// gave <b>6/5/3/3/3</b>, where the faculty's own document and one call of 20 both give
/// <b>4/4/4/4/4</b>.</para>
/// <para>The key is the window as well as the stage: with L &gt; 1 a stage holds several distinct
/// runs at once (the 5th year has {A,B,C} in périodes 1-3, {E,H,I} in 4-6 and {D,F,G} in 7-9), and
/// those are three separate blocks, not one. <c>RotationTiling</c> gives concurrent partitions
/// identical runs, so equality of the period list is the right test; a plan that ever staggered
/// them would simply fall back to today's per-partition behaviour rather than mis-group.</para>
/// </remarks>
internal sealed record ConcurrencyBlock(
    int StageId,
    IReadOnlyList<string> RotationGroups,
    IReadOnlyList<int> PeriodNumbers)
{
    public static List<ConcurrencyBlock> From(IReadOnlyList<PartitionStagePlan> plans) =>
        plans
            // ⚠ An absent window is legitimate and means "every period of the stage" — the matrix
            // says so in its own hint («  vide = toutes  »), and the body can leave the field out
            // entirely. Normalised before it becomes a key, because a null here is a 500 on a
            // request that used to work.
            .Select(p => (p.RotationGroup, p.StageId, Periods: (p.PeriodNumbers ?? []).Order().ToList()))
            .GroupBy(p => (p.StageId, Window: string.Join(",", p.Periods)))
            .Select(g => new ConcurrencyBlock(
                g.Key.StageId,
                g.Select(p => p.RotationGroup).Distinct().Order().ToList(),
                g.First().Periods))
            .ToList();
}
