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
        // ⚠ C'est ici que le mal était fait, et en silence. RemoveChef met le pointeur à null *et*
        // clôt la tenure ouverte ; sans la collection, il ne clôturait rien et l'enregistrement
        // réussissait — laissant un service sans chef mais avec une tenure ouverte. L'affectation
        // suivante butait alors sur l'index unique, et personne ne pouvait plus nommer de chef.
        var service = await dbContext.Services
            .Include(s => s.ChefHistory)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound",
                $"The service with Id = '{request.ServiceId}' was not found."));

        service.RemoveChef();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
