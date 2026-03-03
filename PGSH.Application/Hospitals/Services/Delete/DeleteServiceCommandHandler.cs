using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Delete;

internal class DeleteServiceCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<DeleteServiceCommand>
{
    public async Task<Result> Handle(DeleteServiceCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch the service
        var service = await dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
        {
            return Result.Failure(Error.NotFound("Services.NotFound", "Service not found."));
        }

        // 2. Placeholder for Domain Validation
        // (e.g., Check if students are currently assigned to this service)

        // 3. Perform Deletion
        dbContext.Services.Remove(service);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
