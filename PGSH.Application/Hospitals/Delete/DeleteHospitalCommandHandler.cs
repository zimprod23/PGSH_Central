using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Delete;

internal sealed class DeleteHospitalCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<DeleteHospitalCommand>
{
    public async Task<Result> Handle(DeleteHospitalCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch the hospital with its services
        var hospital = await dbContext.Hospitals
            .Include(h => h.services)
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);

        if (hospital is null)
        {
            return Result.Failure(Error.NotFound("Hospitals.NotFound", "Hospital not found."));
        }

        // 2. Business Rule: Block deletion if clinical services are linked
        if (hospital.services.Any())
        {
            return Result.Failure(Error.Conflict(
                "Hospitals.HasServices",
                "Cannot delete a hospital that contains active services. Move or delete the services first."));
        }

        // 3. Remove and Persist
        dbContext.Hospitals.Remove(hospital);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
