using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.GetById;

internal sealed class GetEmployeeByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetEmployeeByIdQuery, EmployeeResponse>
{
    public async Task<Result<EmployeeResponse>> Handle(
        GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == request.Id)
            .Select(e => new EmployeeResponse(
                e.Id,
                e.Email,
                e.FirstName,
                e.LastName,
                e.CIN,
                e.Gender,
                e.DateOfBirth,
                e.PlaceOfBirth,
                e.Address != null ? e.Address.FullAddress : null,
                e.Grade,
                e.Position,
                e.WorkPlace,
                e.PPR,
                e.Label,
                e.PvSignatureDate,
                dbContext.Services
                    .Where(s => s.Staff.Any(staff => staff.Id == e.Id))
                    .Select(s => new EmployeeServiceAssignment(
                        s.Id,
                        s.Name,
                        s.Hospital.Name,
                        s.ServiceChefId == e.Id))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null
            ? Result.Failure<EmployeeResponse>(EmployeeErrors.NotFound(request.Id))
            : employee;
    }
}
