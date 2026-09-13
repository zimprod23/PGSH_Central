using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Employees;
using PGSH.SharedKernel;
using PGSH.Application.Employees.Search;

namespace PGSH.Application.Employees.GetMany;

internal sealed class GetEmployeesQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetEmployeesQuery, PaginatedResponse<EmployeeSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<EmployeeSummaryResponse>>> Handle(
        GetEmployeesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Employees.AsNoTracking().AsQueryable();

        // Même règle que pour les étudiants, et pour la même raison : « Alami Mohamed » ne trouvait
        // rien. Voir EmployeeSearch.
        query = query.WhereEmployeeMatches(request.SearchTerm, e => e);

        if (request.Grade.HasValue)
            query = query.Where(e => e.Grade == request.Grade.Value);

        if (request.Position.HasValue)
            query = query.Where(e => e.Position == request.Position.Value);

        if (request.ServiceId.HasValue)
        {
            int svcId = request.ServiceId.Value;
            query = query.Where(e =>
                dbContext.Services
                    .Where(s => s.Id == svcId)
                    .SelectMany(s => s.Staff)
                    .Select(s => s.Id)
                    .Contains(e.Id));
        }

        if (request.HospitalId.HasValue)
        {
            int hospId = request.HospitalId.Value;
            query = query.Where(e =>
                dbContext.Services
                    .Where(s => s.HospitalId == hospId)
                    .SelectMany(s => s.Staff)
                    .Select(s => s.Id)
                    .Contains(e.Id));
        }

        var response = await query
            .OrderBy(e => e.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                e => new EmployeeSummaryResponse(
                    e.Id, e.Email, e.FirstName, e.LastName,
                    e.PPR, e.Grade, e.Position, e.WorkPlace),
                cancellationToken);

        return Result.Success(response);
    }
}
