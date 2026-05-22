using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.ServicePeriods.Complete;

internal sealed class CompleteServicePeriodCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CompleteServicePeriodCommand>
{
    public async Task<Result> Handle(CompleteServicePeriodCommand request, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstOrDefaultAsync(
                a => a.ServicePeriods.Any(p => p.Id == request.PeriodId),
                cancellationToken);

        if (assignment is null)
            return Result.Failure(StageErrors.PeriodNotFound(request.PeriodId));

        var result = assignment.CompletePeriod(request.PeriodId);
        if (result.IsFailure)
            return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
