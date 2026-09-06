using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Gives an affectation exactly the périodes the <b>other members of its cohorte</b> hold — the
/// materialisation « changement de groupe » needs, where the promise is that the student becomes
/// indistinguishable from someone the répartition had put there from the start.
/// </summary>
/// <remarks>
/// <para>It is deliberately <i>not</i> a fourth copy of a publication. Three already exist and each
/// answers a different question: <c>SchedulePublisher</c> publishes a plan, <c>LateArrivalScheduler</c>
/// gives a newcomer the windows that have not closed, and
/// <c>MidStageTransferRescheduler.MaterializeAtTargetAsync</c> hands a transferred student the target's
/// grid whether or not it was ever published. This one reproduces a state that already exists.</para>
///
/// <para>⚠ <b>Only cells the cohorte is actually published on.</b> A cell is a plan; a période is the
/// student standing there. Materialising every cell would give a moved student a rotation his new
/// colleagues do not have, on a cohorte nobody ever published — visible on the chef's worklist,
/// counted in the service's effectif, and impossible to tell from a real publication.</para>
///
/// <para>⚠ <b>The stays come from <see cref="CohortStayFolder"/>, never one période per cell.</b> Under
/// <see cref="StageRotationMode.SingleService"/> a cohorte's run of <i>kₛ</i> columns is one période
/// carrying one evaluation; built per cell the moved student would hold <i>kₛ</i> of them, be asked for
/// <i>kₛ</i> marks, and average differently from every classmate. That is the rule the publisher
/// applies, which is why it now lives in the domain rather than inside it.</para>
/// </remarks>
internal sealed class CohortMemberScheduler(IApplicationDbContext dbContext)
{
    /// <param name="PeriodsCreated">Périodes written from the target cohortes' published cells.</param>
    /// <param name="PeriodsReplaced">Grid-linked périodes of the former cohortes, dropped.</param>
    /// <param name="AdHocPeriodsKept">
    /// Périodes hanging off no cell — a délocalisation, a revalidation, imported history. None came
    /// from a répartition and none can be reproduced by one, so they travel with the affectation
    /// untouched. Reported for the same reason <c>UnpublishCohortScheduleCommand</c> reports them.
    /// </param>
    public sealed record Outcome(int PeriodsCreated, int PeriodsReplaced, int AdHocPeriodsKept);

    /// <summary>
    /// Rebuilds every listed affectation's grid-linked périodes against the cohorte it now points at.
    /// </summary>
    /// <remarks>
    /// ⚠ Does <b>not</b> save: the caller is correcting a roster, which is several writes that have to
    /// land together — the group pointer, the affectations, and these périodes.
    /// </remarks>
    public async Task<Outcome> RematerializeAsync(
        IReadOnlyCollection<InternshipAssignment> assignments, CancellationToken ct)
    {
        if (assignments.Count == 0)
            return new Outcome(0, 0, 0);

        var cohortIds = assignments.Select(a => a.CurrentCohortId).Distinct().ToList();

        var cells = await CellsQuery(dbContext, cohortIds).ToListAsync(ct);
        var cellIds = cells.ConvertAll(c => c.Id);

        var published = await dbContext.PublishedAmongAsync(cellIds, ct);
        var states = await CellStatesQuery(dbContext, cellIds).ToListAsync(ct);

        // A cell is started once any non-interrupted période standing on it is; complete once one is
        // closed. Read from the périodes rather than from the calendar for the reason the chef worklist
        // exists to make visible: the two disagree whenever a stage runs late, and it is the périodes
        // the chef's screen follows.
        var startedCells  = states.Where(s => s.IsStarted).Select(s => s.CellId).ToHashSet();
        var completeCells = states.Where(s => s.IsComplete).Select(s => s.CellId).ToHashSet();

        var staysByCohort = cells
            .Where(c => published.Contains(c.Id))
            .GroupBy(c => c.CohortId)
            .ToDictionary(
                g => g.Key,
                g => CohortStayFolder.Fold(
                    g.Select(c => new CohortCell(c.Id, c.PeriodNumber, c.ServiceId, c.StartDate, c.EndDate)),
                    g.First().RotationMode));

        int created = 0, replaced = 0, adHoc = 0;

        foreach (var assignment in assignments)
        {
            replaced += assignment.RemovePublishedPeriods();
            adHoc += assignment.ServicePeriods.Count;

            if (!staysByCohort.TryGetValue(assignment.CurrentCohortId, out var stays))
                continue;

            foreach (var stay in stays)
            {
                // Do NOT pre-set the Id of the période or of its coverage rows: they are children of an
                // already-tracked affectation, and a store-generated key set by hand makes EF classify
                // them Modified — an UPDATE of a row that does not exist. See Delocalize.
                var period = new ServicePeriod
                {
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = stay.ServiceId,
                    CohortSlotAssignmentId = stay.LeadCellId,
                    StartDate              = stay.StartDate,
                    EndDate                = stay.EndDate,
                    IsStarted              = startedCells.Contains(stay.LeadCellId),
                    IsComplete             = completeCells.Contains(stay.LeadCellId),
                };

                foreach (int cellId in stay.CellIds)
                    period.SlotCoverage.Add(new ServicePeriodSlotCoverage
                    {
                        CohortSlotAssignmentId = cellId,
                    });

                assignment.ServicePeriods.Add(period);
                created++;
            }

            assignment.SyncStatusAfterReschedule(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        return new Outcome(created, replaced, adHoc);
    }

    internal sealed record CellRow(
        int Id, int CohortId, int ServiceId, int PeriodNumber,
        DateOnly StartDate, DateOnly EndDate, StageRotationMode RotationMode);

    internal sealed record CellState(int CellId, bool IsStarted, bool IsComplete);

    /// <remarks>
    /// Named, and flat, so <c>SqlTranslationTests</c> can compile it without a database. The rotation
    /// mode is reached through <c>Cohort.Stage</c> — two navigations into a constructor projection,
    /// which translates; folding the cohorte's cells into a collection inside the projection is the
    /// shape Npgsql refuses.
    /// </remarks>
    internal static IQueryable<CellRow> CellsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => cohortIds.Contains(sa.CohortId))
            .Select(sa => new CellRow(
                sa.Id,
                sa.CohortId,
                sa.ServiceId,
                sa.StageSlot.PeriodNumber,
                sa.StageSlot.StartDate,
                sa.StageSlot.EndDate,
                sa.Cohort.Stage.RotationMode));

    /// <remarks>
    /// ⚠ Keyed on the <b>lead</b> cell, which is what a période's foreign key names. That is exactly
    /// the right key here and nowhere else: this asks « où en est la rotation qui part de cette
    /// cellule ? », not « cette cellule est-elle publiée ? » — the second is <c>PublishedCells</c>'
    /// question and is answered from the coverage table.
    /// </remarks>
    internal static IQueryable<CellState> CellStatesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cellIds) =>
        dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.CohortSlotAssignmentId != null
                     && cellIds.Contains(p.CohortSlotAssignmentId!.Value)
                     && !p.IsInterrupted)
            .Select(p => new CellState(p.CohortSlotAssignmentId!.Value, p.IsStarted, p.IsComplete));
}
