using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.Delete;

internal sealed class DeleteEmployeeCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteEmployeeCommand>
{
    public async Task<Result> Handle(
        DeleteEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

        if (employee is null)
            return Result.Failure(EmployeeErrors.NotFound(request.Id));

        dbContext.Employees.Remove(employee);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
