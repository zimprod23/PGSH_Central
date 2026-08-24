using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Gives a student who joins a roster after its schedule was published the rotations he can still do.
/// </summary>
/// <remarks>
/// <para><b>Only windows that have not closed.</b> A stage the roster finished in October is not
/// invented for someone who registered in January: the assignment exists — he owes the stage, and it
/// shows on his dossier as unserved — but no <see cref="ServicePeriod"/> claims he stood in a service
/// on days he was not enrolled. That is the difference from
/// <see cref="MidStageTransferRescheduler.MaterializeAtTargetAsync"/>, which materialises closed cells
/// too, and rightly: a transferred student <em>did</em> serve those periods, with another group.</para>
///
/// <para>A cell still open gets a started period, so the student appears in the chef's list the same
/// day. A cell yet to open gets an unstarted one, exactly as the publication would have written it.</para>
/// </remarks>
internal sealed class LateArrivalScheduler(IApplicationDbContext dbContext)
{
    public sealed record Outcome(int PeriodsCreated, int WindowsAlreadyClosed);

    public async Task<Outcome> MaterializeRemainingAsync(
        IReadOnlyList<InternshipAssignment> assignments,
        DateOnly asOf,
        CancellationToken ct)
    {
        if (assignments.Count == 0)
            return new Outcome(0, 0);

        var cohortIds = assignments.Select(a => a.CurrentCohortId).Distinct().ToList();

        var cells = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => cohortIds.Contains(sa.CohortId))
            .Select(sa => new
            {
                sa.Id,
                sa.CohortId,
                sa.ServiceId,
                sa.StageSlot.StartDate,
                sa.StageSlot.EndDate,
            })
            .ToListAsync(ct);

        if (cells.Count == 0)
            return new Outcome(0, 0);

        // A cell the roster has actually started, rather than one whose dates merely say it should
        // have. The two disagree whenever a stage is running late, and the chef's screen follows the
        // periods, not the calendar.
        var cellIds = cells.Select(c => c.Id).ToList();
        var startedCellIds = (await dbContext.ServicePeriods
                .AsNoTracking()
                .Where(p => p.CohortSlotAssignmentId != null
                         && cellIds.Contains(p.CohortSlotAssignmentId!.Value)
                         && p.IsStarted && !p.IsInterrupted)
                .Select(p => p.CohortSlotAssignmentId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var byCohort = cells.GroupBy(c => c.CohortId).ToDictionary(g => g.Key, g => g.ToList());

        int created = 0, closed = 0;

        foreach (var assignment in assignments)
        {
            if (!byCohort.TryGetValue(assignment.CurrentCohortId, out var cohortCells))
                continue;

            var covered = assignment.ServicePeriods
                .Where(p => p.CohortSlotAssignmentId is not null)
                .Select(p => p.CohortSlotAssignmentId!.Value)
                .ToHashSet();

            foreach (var cell in cohortCells)
            {
                if (covered.Contains(cell.Id)) continue;

                if (cell.EndDate < asOf)
                {
                    closed++;
                    continue;
                }

                var period = new ServicePeriod
                {
                    InternshipAssignmentId = assignment.Id,
                    ServiceId              = cell.ServiceId,
                    CohortSlotAssignmentId = cell.Id,
                    // Never before the day he joined: a period opening in September for someone
                    // registered in January is the same lie as materialising a closed one.
                    StartDate              = cell.StartDate > asOf ? cell.StartDate : asOf,
                    EndDate                = cell.EndDate,
                    IsStarted              = startedCellIds.Contains(cell.Id),
                };

                // ⚠ The coverage row is not bookkeeping. `CohortSlotAssignmentId` answers « did this
                // come from the grid? »; only ServicePeriodSlotCoverage answers « is *this cell*
                // published? », and that is what PublishedCells — and so RotationArranger,
                // DeleteStageSlot, ClearCohortSlotAssignment and ClearSlotAssignments — actually read.
                // Without it the newcomer's cell reads as free: a later auto-arrange rewrites it with
                // another service while his période keeps naming the old one, and DeleteStageSlot lets
                // the column go out from under him.
                period.SlotCoverage.Add(new ServicePeriodSlotCoverage
                {
                    CohortSlotAssignmentId = cell.Id,
                });

                assignment.ServicePeriods.Add(period);

                created++;
            }
        }

        return new Outcome(created, closed);
    }
}
