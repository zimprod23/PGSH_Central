using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

internal sealed class ValidateCohortAssignmentsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<ValidateCohortAssignmentsCommand, int>
{
    public async Task<Result<int>> Handle(
        ValidateCohortAssignmentsCommand request, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId
                     && a.Status == InternshipStatus.Evaluated)
            .ToListAsync(cancellationToken);

        int validated = 0;
        foreach (var a in assignments)
        {
            var r = a.Validate();
            if (r.IsSuccess) validated++;
        }

        if (validated > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(validated);
    }
}
