using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Staff;

internal sealed class RemoveStaffCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<RemoveStaffCommand>
{
    public async Task<Result> Handle(
        RemoveStaffCommand request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .Include(s => s.Staff)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Services.NotFound",
                $"The service with Id = '{request.ServiceId}' was not found."));

        if (!service.Staff.Any(e => e.Id == request.EmployeeId))
            return Result.Failure(EmployeeErrors.NotAStaffMember);

        var employee = service.Staff.First(e => e.Id == request.EmployeeId);
        service.RemoveStaff(employee);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
