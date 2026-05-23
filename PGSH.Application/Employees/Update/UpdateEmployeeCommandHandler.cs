using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.Update;

internal sealed class UpdateEmployeeCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateEmployeeCommand>
{
    public async Task<Result> Handle(
        UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

        if (employee is null)
            return Result.Failure(EmployeeErrors.NotFound(request.Id));

        bool emailTaken = await dbContext.Users
            .AnyAsync(u => u.Email == request.Email && u.Id != request.Id, cancellationToken);

        if (emailTaken)
            return Result.Failure(EmployeeErrors.DuplicateEmail(request.Email));

        if (!string.IsNullOrWhiteSpace(request.PPR))
        {
            bool pprTaken = await dbContext.Employees
                .AnyAsync(e => e.PPR == request.PPR && e.Id != request.Id, cancellationToken);

            if (pprTaken)
                return Result.Failure(EmployeeErrors.DuplicatePPR(request.PPR));
        }

        employee.Email          = request.Email;
        employee.FirstName      = request.FirstName;
        employee.LastName       = request.LastName;
        employee.CIN            = request.CIN;
        employee.Gender         = request.Gender;
        employee.DateOfBirth    = request.DateOfBirth;
        employee.PlaceOfBirth   = request.PlaceOfBirth;
        employee.Address        = request.FullAddress;
        employee.Grade          = request.Grade;
        employee.Position       = request.Position;
        employee.WorkPlace      = request.WorkPlace;
        employee.PPR            = request.PPR;
        employee.Label          = request.Label;
        employee.PvSignatureDate = request.PvSignatureDate;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
