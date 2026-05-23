using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Chef;

internal sealed class RemoveChefCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<RemoveChefCommand>
{
    public async Task<Result> Handle(
        RemoveChefCommand request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound",
                $"The service with Id = '{request.ServiceId}' was not found."));

        service.RemoveChef();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
