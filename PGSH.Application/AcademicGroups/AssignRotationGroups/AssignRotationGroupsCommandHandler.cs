using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Repartition;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <remarks>
/// ⚠ <b>Chaque sortie en succès atteint un <c>SaveChanges</c>, y compris celles qui n'écrivent rien.</b>
/// <c>AuditLogPipelineBehavior</c> met la ligne du registre en attente <em>avant</em> le handler et
/// seul le <c>SaveChanges</c> de celui-ci la valide — c'est ce qui fait qu'un acte refusé n'écrit rien,
/// et c'est aussi ce qui faisait qu'un acte <em>réussi sans effet</em> n'écrivait rien non plus. Deux
/// sorties étaient dans ce cas ici, dont « toutes les promotions portent déjà leur partition », qui est
/// le rejeu ordinaire du bouton. « Personne n'a joué cet acte » et « quelqu'un l'a joué sans effet »
/// sont deux événements sans rapport, et le registre existe pour les départager.
/// </remarks>
internal sealed class AssignRotationGroupsCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<AssignRotationGroupsCommand, PartitionAssignmentResult>
{
    public async Task<Result<PartitionAssignmentResult>> Handle(
        AssignRotationGroupsCommand request, CancellationToken cancellationToken)
    {
        if (request.PartitionCount < 1)
            return Result.Failure<PartitionAssignmentResult>(
                Error.Validation("Partitions.InvalidCount", "Partition count must be at least 1."));

        // Both are nullable only so that an omitted query-string value reaches the validator instead
        // of throwing in routing; by here the validator has refused either absence in words.
        int academicYearId = request.AcademicYearId!.Value;
        int levelId = request.LevelId!.Value;

        // A partition divides a promotion. « Retrait » (year 0) is a withdrawal marker the legacy
        // import kept as a level — see Level.IsPromotion — so cutting it would describe a division of
        // the withdrawn, and it is exactly how one of its rosters came to carry a label.
        // ⚠ The clear is deliberately NOT guarded: it is how such a label is taken back off.
        var level = await dbContext.Levels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == levelId, cancellationToken);

        if (level is null)
            return Result.Failure<PartitionAssignmentResult>(LevelErrors.NotFound(levelId));

        if (!level.IsPromotion)
            return Result.Failure<PartitionAssignmentResult>(
                LevelErrors.NotAPromotion(level.Label ?? $"niveau {levelId}"));

        // Partitions are scoped per (year, level): different levels have different partition counts,
        // and one count applied across them is not a cut of anything.
        //
        // ⚠ The promotion is read from LevelId alone. Falling back to "has a registration at that
        // level" was how legacy rosters — which carried no LevelId — were reached, and it is exactly
        // wrong here: it also reaches « Non réparti », which holds every promotion's unassigned
        // students at once, so cutting one level handed a partition label to a bucket of 4,725 people.
        // SplitAcademicGroupsPerLevel gave every real roster its promotion; the only rows still
        // without one are the buckets, and a bucket is not a rotation partition. Equality against a
        // non-null LevelId excludes them by construction — which is why the parameter is required.
        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == academicYearId && g.LevelId == levelId)
            .OrderBy(g => g.GroupNumber)
            .ToListAsync(cancellationToken);

        // Une promotion sans roster n'a rien à découper : l'acte a eu lieu, il n'a rien fait, et il
        // s'enregistre avec ses zéros plutôt que de disparaître.
        if (groups.Count == 0)
        {
            auditTrail.RecordOutcome(("labeled", 0), ("reassigned", 0), ("totalGroups", 0));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PartitionAssignmentResult(0, 0, 0, 0, []);
        }

        var groupIds = groups.Select(g => g.Id).ToList();

        Dictionary<int, string> assignments;
        int reassigned = 0;

        if (request.Reassign)
        {
            // A cell backed by a ServicePeriod is an execution record: students have been sent there,
            // and possibly already served. Re-cutting the partitions under it would leave the published
            // plan describing a partitioning that no longer exists, so it is refused outright rather
            // than reported — unlike the merely-planned cells below, which an arrange can rebuild.
            int publishedCells = await dbContext.ServicePeriods
                .CountAsync(p => p.CohortSlotAssignmentId != null
                              && groupIds.Contains(p.CohortSlotAssignment!.Cohort.AcademicGroupId),
                    cancellationToken);

            if (publishedCells > 0)
                return Result.Failure<PartitionAssignmentResult>(
                    PartitionErrors.CannotReassignPublished(publishedCells));

            assignments = PartitionAllocator.ReassignAll(
                groupIds, request.PartitionCount, request.Strategy);

            reassigned = groups.Count(g => g.RotationGroup is not null
                                        && assignments.GetValueOrDefault(g.Id) != g.RotationGroup);
        }
        else
        {
            assignments = PartitionAllocator.AssignUnlabelled(
                groups.Select(g => (g.Id, g.RotationGroup)).ToList(),
                request.PartitionCount,
                request.Strategy);
        }

        int labeled = groups.Count(g => g.RotationGroup is null && assignments.ContainsKey(g.Id));

        foreach (var group in groups.Where(g => assignments.ContainsKey(g.Id)))
            group.RotationGroup = assignments[group.Id];

        // Counted after the new labels are known, because what matters is how many planned cells sit on
        // a group whose partition actually moved — not how many exist.
        int plannedCellsAffected = reassigned == 0
            ? 0
            : await dbContext.CohortSlotAssignments
                .CountAsync(a => groupIds.Contains(a.Cohort.AcademicGroupId), cancellationToken);

        // ⚠ Le code seul ne dit pas ce que l'acte a emporté : « découper une promotion vierge en dix »
        // et « rejouer le bouton sur une promotion déjà découpée » sont la même commande et deux
        // événements sans rapport. Le constat voyage avec l'entrée.
        auditTrail.RecordOutcome(
            ("labeled", labeled),
            ("reassigned", reassigned),
            ("totalGroups", groups.Count),
            ("plannedCellsAffected", plannedCellsAffected));

        // ⚠ Inconditionnel. Sous `if (assignments.Count > 0)` un rejeu sur une promotion dont chaque
        // roster porte déjà sa partition — le cas ordinaire — n'appelait jamais `SaveChanges`, donc
        // l'entrée mise en attente par le pipeline mourait avec la requête et le registre disait que
        // personne n'avait rien fait.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PartitionAssignmentResult(
            labeled,
            reassigned,
            groups.Count,
            plannedCellsAffected,
            Membership(groups));
    }

    /// <summary>Each partition's membership, printed the way the répartition prints a cell.</summary>
    private static List<PartitionMembership> Membership(List<AcademicGroup> groups) =>
        groups
            .Where(g => g.RotationGroup is not null)
            .GroupBy(g => g.RotationGroup!)
            .OrderBy(g => g.Key)
            .Select(g => new PartitionMembership(
                g.Key,
                g.Count(),
                GroupNumberRanges.Format(g.Select(x => x.GroupNumber))))
            .ToList();
}
