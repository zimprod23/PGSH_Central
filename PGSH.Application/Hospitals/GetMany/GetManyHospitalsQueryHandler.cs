using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.GetMany;

internal sealed class GetManyHospitalsQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetHospitalsQuery, PaginatedResponse<HospitalSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<HospitalSummaryResponse>>> Handle(GetHospitalsQuery request, CancellationToken cancellationToken)
    {
        // 1. Setup Query
        IQueryable<Hospital> query = dbContext.Hospitals
            .AsNoTracking()
            .Include(h => h.Center);

        // 2. Filter by Center if provided
        if (request.CenterId.HasValue)
        {
            query = query.Where(h => h.CenterId == request.CenterId.Value);
        }

        // 3. Apply Search Filter
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(h =>
                h.Name.ToLower().Contains(term) ||
                h.City.ToLower().Contains(term));
        }

        // 4. Count Total
        int totalCount = await query.CountAsync(cancellationToken);

        // 5. Projection & Pagination
        var items = await query
            .OrderBy(h => h.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(h => new HospitalSummaryResponse(
                h.Id,
                h.Name,
                h.CenterId,
                h.Center.Name, // From the Include
                h.HospitalType.ToString(),
                h.City,
                h.Email))
            .ToListAsync(cancellationToken);

        // 6. Wrap in Result
        var response = new PaginatedResponse<HospitalSummaryResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
