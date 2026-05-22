using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Start;

internal sealed class StartAssignmentCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<StartAssignmentCommand>
{
    public async Task<Result> Handle(StartAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.InternshipAssignments
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken);

        if (assignment is null)
            return Result.Failure(StageErrors.AssignmentNotFound(request.AssignmentId));

        var result = assignment.Start();
        if (result.IsFailure)
            return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
