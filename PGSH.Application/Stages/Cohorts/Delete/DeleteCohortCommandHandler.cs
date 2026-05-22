using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Delete;

internal sealed class DeleteCohortCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteCohortCommand>
{
    public async Task<Result> Handle(DeleteCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .FirstOrDefaultAsync(c => c.Id == request.CohortId, cancellationToken);

        if (cohort is null)
            return Result.Failure(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with Id = '{request.CohortId}' was not found."));

        dbContext.Cohorts.Remove(cohort);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
