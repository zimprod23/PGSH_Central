using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Staff;

internal sealed class AssignStaffCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignStaffCommand>
{
    public async Task<Result> Handle(
        AssignStaffCommand request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .Include(s => s.Staff)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound",
                $"The service with Id = '{request.ServiceId}' was not found."));

        var employee = await dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken);

        if (employee is null)
            return Result.Failure(EmployeeErrors.NotFound(request.EmployeeId));

        if (service.Staff.Any(e => e.Id == request.EmployeeId))
            return Result.Failure(EmployeeErrors.AlreadyInStaff);

        service.AddStaff(employee);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
