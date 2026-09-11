using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delete;

/// <summary>
/// Removes a stage, once nothing names it any more.
/// </summary>
/// <remarks>
/// <para>⚠ <b>An ungated delete here was destructive in two different ways, and neither announced
/// itself.</b> Measured against the live schema on 2026-09-11:</para>
///
/// <list type="bullet">
///   <item><c>Cohorts.StageId</c> and <c>CurriculumStages.StageId</c> are <c>RESTRICT</c>, and
///   <c>ObjectiveScores</c> restricts one level down through <c>StageObjectives</c> — so the delete
///   came back as a raw foreign-key violation, i.e. a <b>500</b> whose only content was the name of a
///   PostgreSQL constraint. That is what « supprimer un stage rattaché à un CNPN » produced.</item>
///   <item><c>StageSlots</c>, <c>StageAllowedServices</c> and <c>StageObjectives</c> are
///   <b>CASCADE</b> — so a stage with an axis already laid loses its créneaux, <i>for every year</i>,
///   together with the service order and the « Réservé » modes, silently.</item>
/// </list>
///
/// <para>The cascade itself is the right bargain and is kept: a créneau of a stage that no longer
/// exists records nothing, exactly as an empty roster of a deleted year records nothing
/// (<c>DeleteAcademicYearCommand</c> strikes the same one). What was missing is that <b>nobody was
/// told how much it took</b> — so the counts go to the register, which is the only place that can
/// still answer it afterwards.</para>
/// </remarks>
internal sealed class DeleteStageCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<DeleteStageCommand>
{
    public async Task<Result> Handle(DeleteStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var holdings = await DescribeHoldingsAsync(request.StageId, cancellationToken);
        if (holdings.Count > 0)
            return Result.Failure(StageErrors.StillInUse(stage.Name, holdings));

        // Read before the delete: after it, there is nothing left to count.
        auditTrail.RecordOutcome(
            ("stageName", stage.Name),
            ("levelId", stage.LevelId),
            ("slotsRemoved", await dbContext.StageSlots
                .CountAsync(s => s.StageId == request.StageId, cancellationToken)),
            ("allowedServicesRemoved", await dbContext.StageAllowedServices
                .CountAsync(a => a.StageId == request.StageId, cancellationToken)),
            ("objectivesRemoved", await dbContext.StageObjectives
                .CountAsync(o => o.StageId == request.StageId, cancellationToken)));

        dbContext.Stages.Remove(stage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Every reason the stage cannot go, in the words the refusal will use.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Counted, never short-circuited on the first hit.</b> A user who removes the stage from its
    /// CNPN only to be told next that it carries cohortes has been sent round the loop twice — and the
    /// second trip looks like the first fix having failed.
    /// </remarks>
    private async Task<List<string>> DescribeHoldingsAsync(int stageId, CancellationToken ct)
    {
        var holdings = new List<string>();

        int cohorts = await dbContext.Cohorts.CountAsync(c => c.StageId == stageId, ct);
        if (cohorts > 0) holdings.Add($"{cohorts} cohorte(s)");

        // The texts are named, not counted: « 2 CNPN » leaves the operator to find which, and the
        // whole point of the refusal is to tell him where to go next.
        var texts = await dbContext.CurriculumStages
            .Where(cs => cs.StageId == stageId)
            .Select(cs => cs.Curriculum.CnpnVersion.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct);

        if (texts.Count > 0)
            holdings.Add($"le programme CNPN {string.Join(" et ", texts)}");

        // Restricts one level down, through StageObjectives — so without this the refusal would name
        // a constraint on a table the operator has never heard of.
        int scores = await dbContext.ObjectiveScores
            .CountAsync(s => s.StageObjective.StageId == stageId, ct);
        if (scores > 0) holdings.Add($"{scores} note(s) par objectif");

        return holdings;
    }
}
