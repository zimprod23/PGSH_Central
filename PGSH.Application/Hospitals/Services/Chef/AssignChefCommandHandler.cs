using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Chef;

internal sealed class AssignChefCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignChefCommand>
{
    public async Task<Result> Handle(
        AssignChefCommand request, CancellationToken cancellationToken)
    {
        // ⚠ ChefHistory est incluse parce que l'agrégat la *modifie* : AssignChef clôt la tenure
        // ouverte avant d'en ouvrir une autre. Non incluse, la collection est vide et indiscernable
        // d'une absence de tenure — rien n'est clôturé, une seconde ligne ouverte est ajoutée, et
        // l'index unique filtré « une seule tenure ouverte par service » répond 23505. Mesuré sur la
        // base vivante le 23/09/2026 : Pédiatrie1 et Pédiatrie2 ne pouvaient plus recevoir de chef.
        var service = await dbContext.Services
            .Include(s => s.Staff)
            .Include(s => s.ChefHistory)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound",
                $"The service with Id = '{request.ServiceId}' was not found."));

        var employee = service.Staff.FirstOrDefault(e => e.Id == request.EmployeeId);
        if (employee is null)
            return Result.Failure(EmployeeErrors.NotInStaff);

        var result = service.AssignChef(employee);
        if (result.IsFailure) return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
