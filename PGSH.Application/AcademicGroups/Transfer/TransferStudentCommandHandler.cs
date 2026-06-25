using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Transfer;

internal sealed class TransferStudentCommandHandler(IApplicationDbContext dbContext)
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

        bool targetGroupExists = await dbContext.AcademicGroups
            .AnyAsync(g => g.Id == request.TargetGroupId, cancellationToken);

        if (!targetGroupExists)
            return Result.Failure(Error.NotFound(
                "AcademicGroups.NotFound",
                $"Target group '{request.TargetGroupId}' not found."));

        // A definitive move can only ever land on a different group; a temporary loan may target
        // a different group while the student stays registered in their own, so the same-group
        // guard only applies to definitive transfers.
        if (request.Type == TransferType.Definitive
            && registration.AcademicGroupId == request.TargetGroupId)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.SameGroup",
                "The student is already in this group; nothing to transfer."));

        var query = dbContext.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.Cohort)
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

            assignment.TransferToCohort(
                targetCohort.Id,
                request.Reason,
                DateOnly.FromDateTime(DateTime.UtcNow),
                request.Type);
            moved = true;
        }

        if (request.Type == TransferType.Temporary && !moved)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.SameGroup",
                "The selected stage already runs with the target group; nothing to transfer."));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
