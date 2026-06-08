using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
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

        if (registration.AcademicGroupId == request.TargetGroupId)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.SameGroup",
                "The student is already in this group; nothing to transfer."));

        bool targetGroupExists = await dbContext.AcademicGroups
            .AnyAsync(g => g.Id == request.TargetGroupId, cancellationToken);

        if (!targetGroupExists)
            return Result.Failure(Error.NotFound(
                "AcademicGroups.NotFound",
                $"Target group '{request.TargetGroupId}' not found."));

        // Domain method raises StudentGroupTransferredDomainEvent → handler writes GroupTransfer history
        registration.TransferToGroup(request.TargetGroupId, request.Reason);

        // Transfer active internship assignments to the matching cohort in the target group
        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.Cohort)
            .Where(a => a.RegistrationId == request.RegistrationId
                     && a.Status != InternshipStatus.Completed
                     && a.Status != InternshipStatus.Validated
                     && a.Status != InternshipStatus.Rejected)
            .ToListAsync(cancellationToken);

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
                DateOnly.FromDateTime(DateTime.UtcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
