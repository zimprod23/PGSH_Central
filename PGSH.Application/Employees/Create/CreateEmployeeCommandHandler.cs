using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.Create;

internal sealed class CreateEmployeeCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        bool emailTaken = await dbContext.Users
            .AnyAsync(u => u.Email == request.Email, cancellationToken);

        if (emailTaken)
            return Result.Failure<Guid>(EmployeeErrors.DuplicateEmail(request.Email));

        if (!string.IsNullOrWhiteSpace(request.PPR))
        {
            bool pprTaken = await dbContext.Employees
                .AnyAsync(e => e.PPR == request.PPR, cancellationToken);

            if (pprTaken)
                return Result.Failure<Guid>(EmployeeErrors.DuplicatePPR(request.PPR));
        }

        var employee = new Employee
        {
            Id             = Guid.NewGuid(),
            Email          = request.Email,
            FirstName      = request.FirstName,
            LastName       = request.LastName,
            CIN            = request.CIN,
            Gender         = request.Gender,
            DateOfBirth    = request.DateOfBirth,
            PlaceOfBirth   = request.PlaceOfBirth,
            Address        = request.FullAddress,
            Grade          = request.Grade,
            Position       = request.Position,
            WorkPlace      = request.WorkPlace,
            PPR            = request.PPR,
            Label          = request.Label,
            PvSignatureDate = request.PvSignatureDate,
        };

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);
        return employee.Id;
    }
}
