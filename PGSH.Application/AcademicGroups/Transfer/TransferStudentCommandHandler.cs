using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Transfer;

internal sealed class TransferStudentCommandHandler(
    IApplicationDbContext dbContext,
    MidStageTransferRescheduler rescheduler)
    : ICommandHandler<TransferStudentCommand>
{
    public async Task<Result> Handle(TransferStudentCommand request, CancellationToken cancellationToken)
    {
        var registration = await dbContext.Registrations
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
            return Result.Failure(Error.NotFound(
                "Registrations.NotFound",
                $"Registration '{request.RegistrationId}' not found."));

        var target = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.TargetGroupId)
            .Select(g => new { g.Label, g.AcademicYearId, g.LevelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
            return Result.Failure(AcademicGroupErrors.NotFound(request.TargetGroupId));

        // ⚠ This is the only write that can put a registration in a roster of another promotion, and
        // nothing downstream would object: the FK is satisfied, and every later guard is keyed on the
        // roster the registration *claims*. A 3rd-year moved into a 5th-year roster is then affected
        // to that roster's cohorts — 5th-year stages he does not owe — and counted against the 5th
        // year's service quotas. Refused here rather than repaired later, which is what
        // SplitAcademicGroupsPerLevel had to do for 1,003 rows.
        if (target.AcademicYearId != registration.AcademicYearId)
            return Result.Failure(AcademicGroupErrors.TargetGroupInAnotherYear(
                target.Label,
                await YearLabelAsync(target.AcademicYearId, cancellationToken),
                await YearLabelAsync(registration.AcademicYearId, cancellationToken)));

        // A level-less target is « Non réparti », and putting a student back in the bucket is a
        // legitimate un-assignment rather than a cross-promotion move — the bucket belongs to no
        // promotion precisely so that it can hold all of them.
        if (target.LevelId is { } targetLevel && targetLevel != registration.LevelId)
            return Result.Failure(AcademicGroupErrors.TargetGroupInAnotherLevel(
                target.Label,
                await LevelLabelAsync(targetLevel, cancellationToken),
                await LevelLabelAsync(registration.LevelId, cancellationToken)));

        // A definitive move can only ever land on a different group; a temporary loan may target
        // a different group while the student stays registered in their own, so the same-group
        // guard only applies to definitive transfers.
        if (request.Type == TransferType.Definitive
            && registration.AcademicGroupId == request.TargetGroupId)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.SameGroup",
                "The student is already in this group; nothing to transfer."));

        // Periods (and the slot cells they were generated from) are loaded for both transfer paths:
        // a mid-stage hand-off re-routes the in-flight rotation, and a plain transfer rehomes the
        // future rotation onto the target group's slots so the new chef gets real, evaluable periods.
        var query = dbContext.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.Cohort)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.CohortSlotAssignment!)
                    .ThenInclude(sa => sa.StageSlot)
            .Where(a => a.RegistrationId == request.RegistrationId
                     && a.Status != InternshipStatus.Completed
                     && a.Status != InternshipStatus.Validated
                     && a.Status != InternshipStatus.Rejected);

        // Temporary: only the named stage's assignment moves, and the registration's group is
        // left untouched so every other stage still runs with the original group.
        if (request.Type == TransferType.Temporary)
            query = query.Where(a => a.Cohort.StageId == request.StageId);
        else
            // Domain method raises StudentGroupTransferredDomainEvent → GroupTransfer history.
            registration.TransferToGroup(request.TargetGroupId, request.Reason);

        var assignments = await query.ToListAsync(cancellationToken);

        if (request.Type == TransferType.Temporary && assignments.Count == 0)
            return Result.Failure(Error.Conflict(
                "Transfers.NoActiveAssignment",
                "The student has no active assignment for the selected stage to transfer."));

        bool moved = false;
        foreach (var assignment in assignments)
        {
            var targetCohort = await dbContext.Cohorts
                .Where(c => c.AcademicGroupId == request.TargetGroupId
                         && c.StageId == assignment.Cohort.StageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (targetCohort is null || targetCohort.Id == assignment.CurrentCohortId) continue;

            var transferDate = DateOnly.FromDateTime(DateTime.UtcNow);
            assignment.TransferToCohort(targetCohort.Id, request.Reason, transferDate, request.Type);
            moved = true;

            // Opt-in forced mid-stage hand-off: re-route the in-flight rotation to the target
            // group's services so the new chef can supervise/evaluate. No-op for assignments
            // that aren't actually mid-stage. Fails if the target group lacks the schedule.
            if (request.Reschedule)
            {
                var reroute = await rescheduler.RerouteAsync(assignment, targetCohort.Id, transferDate, cancellationToken);
                if (reroute.IsFailure)
                    return reroute;
            }
            else
            {
                // Normal transfer: rehome the future rotation onto the target group's slots so the
                // new chef gets actionable, evaluable periods instead of an informational green row.
                var materialize = await rescheduler.MaterializeAtTargetAsync(assignment, targetCohort.Id, transferDate, cancellationToken);
                if (materialize.IsFailure)
                    return materialize;
            }

            // The rescheduler hands the student the target group's current lifecycle state per period;
            // reflect that on the assignment so it isn't left showing "Planifié" (or stuck "Ongoing"
            // after joining a group whose stage is already closed).
            assignment.SyncStatusAfterReschedule(transferDate);
        }

        if (request.Type == TransferType.Temporary && !moved)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.SameGroup",
                "The selected stage already runs with the target group; nothing to transfer."));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // Resolved only on the way to a refusal. The message has to name the two promotions — "ce groupe
    // n'est pas le bon" sends an admin hunting through a list of sixty — but the successful transfer,
    // which is the one that runs thousands of times, should not pay for two extra reads.
    private async Task<string> YearLabelAsync(int academicYearId, CancellationToken ct) =>
        await dbContext.AcademicYears
            .Where(y => y.Id == academicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(ct) ?? $"année {academicYearId}";

    private async Task<string> LevelLabelAsync(int levelId, CancellationToken ct) =>
        await dbContext.Levels
            .Where(l => l.Id == levelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(ct) ?? $"niveau {levelId}";
}
