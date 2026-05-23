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
        var service = await dbContext.Services
            .Include(s => s.Staff)
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
