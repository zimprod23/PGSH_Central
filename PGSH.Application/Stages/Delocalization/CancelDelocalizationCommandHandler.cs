using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

internal sealed class CancelDelocalizationCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CancelDelocalizationCommand>
{
    public async Task<Result> Handle(CancelDelocalizationCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.DelocalizationNotAllowed);
        if (access.IsFailure)
            return access;

        // ⚠ Evaluations Included: the guard inside CancelDelocalization refuses over a recorded
        // verdict, and an un-Included evaluation is indistinguishable from an absent one.
        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
            .FirstOrDefaultAsync(
                a => a.RegistrationId == request.RegistrationId && a.Cohort.StageId == request.StageId,
                cancellationToken);

        if (assignment is null)
            return Result.Failure(StageErrors.NotDelocalized);

        var result = assignment.CancelDelocalization(request.StageId);
        if (result.IsFailure)
            return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
