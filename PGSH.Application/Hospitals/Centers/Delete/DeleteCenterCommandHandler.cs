using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.Delete;

internal sealed class DeleteCenterCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<DeleteCenterCommand>
{
    public async Task<Result> Handle(DeleteCenterCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch the center including its hospitals count
        var center = await dbContext.Centers
            .Include(c => c.Hospitals)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (center is null)
        {
            return Result.Failure(Error.NotFound("Centers.NotFound", $"Center {request.Id} not found."));
        }

        // 2. Business Rule: Prevent deletion if Hospitals are linked
        if (center.Hospitals.Any())
        {
            return Result.Failure(Error.Conflict(
                "Centers.HasHospitals",
                "Cannot delete a center that still contains active hospitals. Delete or move the hospitals first."));
        }

        // 3. Perform Deletion
        dbContext.Centers.Remove(center);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
