using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.DeleteAll;

/// <summary>
/// Removes the rosters in scope, and the cohortes hanging off them.
/// </summary>
/// <remarks>
/// <para>The order it enforces — empty the rosters, then delete them — is what keeps this safe: a
/// roster can only be emptied once its affectations are gone or explicitly dropped, so by the time
/// this runs there is normally nothing left to destroy. The guard stays for the rosters emptied
/// before that rule existed, which left their affectations behind: this is the act that would sweep
/// them away.</para>
///
/// <para>⚠ <b>Every read and every write here is scoped by <c>groupIds</c>, never re-derived from
/// the year.</b> That is the one thing that made the promotion scope a two-line change rather than a
/// rewrite — and the final <c>ExecuteDelete</c> was the exception that had to be corrected, because
/// re-filtering on the year alone would have deleted every promotion's rosters after a refusal check
/// that had only looked at one.</para>
/// </remarks>
internal sealed class DeleteAllGroupsCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader,
    IAuditTrail auditTrail)
    : ICommandHandler<DeleteAllGroupsCommand, int>
{
    /// <summary>
    /// ⚠ <b>Une transaction, parce qu'il y a six suppressions et qu'elles se suivent.</b> Périodes,
    /// historique de cohorte, affectations, cellules, cohortes, groupes : chacune est une instruction
    /// séparée qui atterrit pour de bon dès qu'elle passe. Une requête interrompue entre la troisième
    /// et la quatrième — l'onglet fermé, la connexion coupée : ASP.NET annule le jeton — laissait les
    /// groupes et leurs cohortes en place, la grille pleine de cellules, et <b>plus personne
    /// dedans</b> : un plan complet et confiant pour zéro étudiant, que rien à l'écran ne distingue
    /// d'un plan voulu. Et le registre se taisait, puisque sa ligne est la septième instruction.
    /// </summary>
    public Task<Result<int>> Handle(DeleteAllGroupsCommand request, CancellationToken cancellationToken) =>
        auditTrail.RunAtomicallyAsync(ct => DeleteAsync(request, ct), cancellationToken);

    private async Task<Result<int>> DeleteAsync(DeleteAllGroupsCommand request, CancellationToken cancellationToken)
    {
        int? levelId = request.LevelId;
        string? levelLabel = null;

        if (levelId is not null)
        {
            // ⚠ An unknown level refuses rather than falling through to « no level named ». That is
            // the widening-on-absence defect, on an act that otherwise writes across a whole year.
            levelLabel = await dbContext.Levels
                .Where(l => l.Id == levelId)
                .Select(l => l.Label)
                .FirstOrDefaultAsync(cancellationToken);

            if (levelLabel is null)
                return Result.Failure<int>(LevelErrors.NotFound(levelId.Value));
        }

        var groupIds = await RosterScope.Query(dbContext, request.AcademicYearId, levelId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        // ⚠ Un acte qui ne trouve rien à supprimer est un acte, pas un refus : il s'enregistre, avec
        // son zéro. Sans cela l'absence de ligne recouvrirait deux états — « personne n'a joué cet
        // acte » et « quelqu'un l'a joué sur une promotion déjà vide » — et c'est précisément ce que
        // le registre existe pour départager.
        if (groupIds.Count == 0)
        {
            auditTrail.RecordOutcome(("rostersDeleted", 0), ("cohortsDeleted", 0));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(0);
        }

        int students = await dbContext.Registrations
            .CountAsync(
                r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value),
                cancellationToken);

        if (students > 0)
            return Result.Failure<int>(AcademicGroupErrors.RostersHaveStudents(
                await ScopeLabelAsync(request.AcademicYearId, levelLabel, cancellationToken),
                students,
                groupIds.Count));

        var cohortIds = await dbContext.Cohorts
            .Where(c => groupIds.Contains(c.AcademicGroupId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (cohortIds.Count > 0)
        {
            var toll = await tollReader.ForCohortsAsync(cohortIds, cancellationToken);

            if (toll.IsUnderway)
            {
                string yearLabel = await YearLabelAsync(request.AcademicYearId, cancellationToken);

                return Result.Failure<int>(levelId is null
                    ? AcademicGroupErrors.YearRostersUnderway(
                        yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                        toll.Started, toll.Evaluated, toll.AttendanceDays)
                    : AcademicGroupErrors.PromotionRostersUnderway(
                        levelLabel!, yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                        toll.Started, toll.Evaluated, toll.AttendanceDays));
            }

            var assignmentIds = await dbContext.InternshipAssignments
                .Where(a => cohortIds.Contains(a.CurrentCohortId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            if (assignmentIds.Count > 0)
            {
                await dbContext.ServicePeriods
                    .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.CohortMembership
                    .Where(m => assignmentIds.Contains(m.InternshipAssignmentId)
                             || cohortIds.Contains(m.CohortId))
                    .ExecuteDeleteAsync(cancellationToken);

                await dbContext.InternshipAssignments
                    .Where(a => cohortIds.Contains(a.CurrentCohortId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await dbContext.CohortSlotAssignments
                .Where(a => cohortIds.Contains(a.CohortId))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.Cohorts
                .Where(c => groupIds.Contains(c.AcademicGroupId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        int deleted = await dbContext.AcademicGroups
            .Where(g => groupIds.Contains(g.Id))
            .ExecuteDeleteAsync(cancellationToken);

        auditTrail.RecordOutcome(
            ("rostersDeleted", deleted),
            ("cohortsDeleted", cohortIds.Count));

        // ⚠ Cet acte n'écrivait rien au registre, et il portait pourtant IAuditableCommand depuis la
        // phase 20. Tout passe par ExecuteDelete, qui contourne le change tracker, et rien n'appelait
        // SaveChanges : la ligne ajoutée par AuditLogPipelineBehavior restait en attente jusqu'à la
        // fin de la requête, puis disparaissait avec la portée. « Supprimer les groupes » a donc
        // détruit des rosters et leurs cohortes sans laisser une ligne, exactement comme les actes
        // qui ne déclaraient rien du tout. Mesuré le 10/09/2026.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(deleted);
    }

    /// <summary>What the refusal calls the set it is refusing over — the promotion, or the year.</summary>
    private async Task<string> ScopeLabelAsync(
        int academicYearId, string? levelLabel, CancellationToken cancellationToken)
    {
        string yearLabel = await YearLabelAsync(academicYearId, cancellationToken);
        return levelLabel is null ? yearLabel : $"{levelLabel} ({yearLabel})";
    }

    private async Task<string> YearLabelAsync(int academicYearId, CancellationToken cancellationToken) =>
        await dbContext.AcademicYears
            .Where(y => y.Id == academicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? $"l'année {academicYearId}";
}
