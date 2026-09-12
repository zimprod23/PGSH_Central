using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.DeleteAll;

/// <summary>
/// Resets one stage's cohortes for one year.
/// </summary>
/// <remarks>
/// <para>The guard is the same as the single-cohorte delete's, read through the same
/// <see cref="AffectationTollReader"/> so the two refusals cannot describe the same rows
/// differently — and it now <b>names</b> what it is refusing over. « des affectations sont déjà en
/// cours » told the admin nothing about which stage, how far along, or what to do next.</para>
/// </remarks>
internal sealed class DeleteAllCohortsCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    AffectationTollReader tollReader,
    IAuditTrail auditTrail)
    : ICommandHandler<DeleteAllCohortsCommand, DeleteAllCohortsResult>
{
    /// <summary>
    /// ⚠ <b>Une transaction : cinq suppressions se suivent, et chacune est définitive dès qu'elle
    /// passe.</b> Interrompue au milieu, « Réinitialiser les cohortes » laissait un stage à moitié
    /// remis à zéro — des cohortes sans affectation, des cellules sans cohorte — sans une ligne au
    /// registre, celle-ci n'étant validée qu'à la fin. Voir <c>DeleteAllGroupsCommandHandler</c>.
    /// </summary>
    public Task<Result<DeleteAllCohortsResult>> Handle(
        DeleteAllCohortsCommand request, CancellationToken cancellationToken) =>
        auditTrail.RunAtomicallyAsync(ct => ResetAsync(request, ct), cancellationToken);

    private async Task<Result<DeleteAllCohortsResult>> ResetAsync(
        DeleteAllCohortsCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<DeleteAllCohortsResult>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (stage is null)
            return Result.Failure<DeleteAllCohortsResult>(StageErrors.NotFound(request.StageId));

        var cohortIds = await dbContext.Cohorts
            .Where(c => c.StageId == request.StageId && c.AcademicGroup.AcademicYearId == yearId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // ⚠ Un stage sans cohorte cette année-là est un acte sans effet, pas un refus : il
        // s'enregistre avec ses zéros. « Réinitialiser » sur une promotion déjà vierge et sur une
        // promotion publiée écriraient sinon la même ligne, et ce sont deux événements sans rapport.
        if (cohortIds.Count == 0)
        {
            RecordOutcome(yearId, 0, 0, 0);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DeleteAllCohortsResult(0, 0, 0);
        }

        var toll = await tollReader.ForCohortsAsync(cohortIds, cancellationToken);

        if (toll.IsUnderway)
        {
            return Result.Failure<DeleteAllCohortsResult>(StageErrors.StageCohortsUnderway(
                stage.Name, yearLabel, cohortIds.Count, toll.Assignments, toll.Periods,
                toll.Started, toll.Evaluated, toll.AttendanceDays));
        }

        var assignmentIds = await dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        int periodsRemoved = 0;

        if (assignmentIds.Count > 0)
        {
            periodsRemoved = await dbContext.ServicePeriods
                .Where(p => assignmentIds.Contains(p.InternshipAssignmentId))
                .ExecuteDeleteAsync(cancellationToken);

            // Memberships belonging to these assignments (the cascade path) and those pointing into
            // these cohortes from assignments elsewhere (the transfer path).
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

        int deleted = await dbContext.Cohorts
            .Where(c => cohortIds.Contains(c.Id))
            .ExecuteDeleteAsync(cancellationToken);

        RecordOutcome(yearId, deleted, assignmentIds.Count, periodsRemoved);

        // ⚠ Tout ce qui précède passe par ExecuteDelete, qui contourne le change tracker : sans ce
        // SaveChanges la ligne de journal mise en attente par AuditLogPipelineBehavior mourrait avec
        // la portée de la requête, et l'acte le plus destructeur de la planification ne laisserait
        // rien. C'est le défaut mesuré le 10/09/2026 sur « Supprimer les groupes ».
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteAllCohortsResult(deleted, assignmentIds.Count, periodsRemoved);
    }

    /// <summary>
    /// Ce que l'acte a emporté, plus l'année qu'il a réellement touchée — la commande ne pouvait pas
    /// la donner, puisqu'une année absente veut dire « celle en cours » et que la résolution est ici.
    /// </summary>
    private void RecordOutcome(int yearId, int cohorts, int affectations, int periods) =>
        auditTrail.RecordOutcome(
            ("academicYearId", yearId),
            ("cohortsRemoved", cohorts),
            ("affectationsRemoved", affectations),
            ("periodsRemoved", periods));
}
