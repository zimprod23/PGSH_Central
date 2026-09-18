using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <summary>
/// Un-partitions a promotion: every group's <c>RotationGroup</c> goes back to null, so the next cut is
/// free to choose any number.
/// </summary>
/// <remarks>
/// <para><b>Why this is needed even though <c>Reassign</c> exists.</b> <c>PartitionAllocator.BuildLabels</c>
/// lets the <em>existing</em> partition count win over the requested one — deliberately, so a gap-fill
/// cannot reshuffle a plan built on the current partitioning. The consequence is that a promotion mistakenly
/// cut into two stays two-way for every later auto-arrange, whatever count is asked for. Clearing is the
/// only way back to "not yet partitioned", and therefore the only way a wrong cut is genuinely undone
/// rather than argued with.</para>
///
/// <para><b>What it does not touch.</b> Nothing points at a partition label: cohorts belong to groups, cells
/// belong to cohorts and slots, periods belong to cells. Clearing the label removes no row and breaks no
/// foreign key — the planning stays exactly as it was and stays executable. What it does mean is that the
/// cells no longer describe any partition, so the crossover they encode has to be rebuilt by arranging
/// again; <see cref="ClearRotationGroupsResult.PlannedCellsAffected"/> is how many.</para>
/// </remarks>
/// <param name="LevelId">
/// The promotion being un-partitioned — required for the same reason the cut is (see
/// <see cref="AssignRotationGroupsCommand"/>): year-wide, this cleared every promotion at once and
/// reached the year's « Non réparti » bucket.
/// </param>
public sealed record ClearRotationGroupsCommand(int? AcademicYearId, int? LevelId)
    : ICommand<ClearRotationGroupsResult>, IAuditableCommand
{
    // ⚠ Nullable to carry an *omission* as far as the validator, not because either is optional —
    // bound non-nullable from the query string they threw in routing before any validator ran, so a
    // blank selector produced a bare 400 with nothing on screen to explain it. Validation runs before
    // the audit behaviour, so the register below reads an already-refused-or-complete command.
    public string AuditAction => "PARTITIONS_CLEARED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId!.Value.ToString();
    public string? AuditMetadata => AuditMetadataJson.Of(("levelId", LevelId!.Value));
}

/// <param name="PlannedCellsAffected">
/// Cells still planned on the partitioning just removed. They survive untouched, but every one of them was
/// placed for a partition that no longer exists, so an arrange is owed.
/// </param>
public sealed record ClearRotationGroupsResult(
    int Cleared,
    int TotalGroups,
    int PlannedCellsAffected);

/// <remarks>
/// ⚠ <b>Comme son aller, chaque sortie en succès atteint un <c>SaveChanges</c>.</b> L'entrée du
/// registre est mise en attente avant le handler et n'est validée que par lui : les deux sorties sans
/// effet — « aucun roster » et « aucune partition à retirer » — n'écrivaient donc rien, et un acte
/// réussi devenait indiscernable d'un acte que personne n'a joué.
/// </remarks>
internal sealed class ClearRotationGroupsCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<ClearRotationGroupsCommand, ClearRotationGroupsResult>
{
    public async Task<Result<ClearRotationGroupsResult>> Handle(
        ClearRotationGroupsCommand request, CancellationToken cancellationToken)
    {
        int academicYearId = request.AcademicYearId!.Value;
        int levelId = request.LevelId!.Value;

        // Same reach as the assign, and for the same reason: the promotion is LevelId, never
        // "somebody at that level is registered here" and never the whole year.
        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == academicYearId && g.LevelId == levelId)
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
        {
            auditTrail.RecordOutcome(("cleared", 0), ("totalGroups", 0), ("plannedCellsAffected", 0));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ClearRotationGroupsResult(0, 0, 0);
        }

        var groupIds = groups.Select(g => g.Id).ToList();

        // Refused for the same reason a re-cut is: a cell backed by a ServicePeriod is an execution
        // record. Students have been sent there, and the printed répartition names the partition they were
        // sent as — removing the label would leave that document unreproducible.
        int publishedCells = await dbContext.ServicePeriods
            .CountAsync(p => p.CohortSlotAssignmentId != null
                          && groupIds.Contains(p.CohortSlotAssignment!.Cohort.AcademicGroupId),
                cancellationToken);

        if (publishedCells > 0)
            return Result.Failure<ClearRotationGroupsResult>(
                PartitionErrors.CannotClearPublished(publishedCells));

        var labelled = groups.Where(g => g.RotationGroup is not null).ToList();

        // Une promotion déjà non partitionnée : l'acte a eu lieu et n'a rien retiré. Il s'enregistre.
        if (labelled.Count == 0)
        {
            auditTrail.RecordOutcome(
                ("cleared", 0), ("totalGroups", groups.Count), ("plannedCellsAffected", 0));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ClearRotationGroupsResult(0, groups.Count, 0);
        }

        int plannedCells = await dbContext.CohortSlotAssignments
            .CountAsync(a => groupIds.Contains(a.Cohort.AcademicGroupId), cancellationToken);

        foreach (var group in labelled)
            group.RotationGroup = null;

        auditTrail.RecordOutcome(
            ("cleared", labelled.Count),
            ("totalGroups", groups.Count),
            ("plannedCellsAffected", plannedCells));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClearRotationGroupsResult(labelled.Count, groups.Count, plannedCells);
    }
}
