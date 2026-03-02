using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Centers.GetById;

internal sealed class GetCenterByIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetCenterByIdQuery, CenterDetailResponse>
{
    public async Task<Result<CenterDetailResponse>> Handle(GetCenterByIdQuery request, CancellationToken cancellationToken)
    {
        // 1. Fetch with Include for all related data
        var center = await dbContext.Centers
            .AsNoTracking()
            .Include(c => c.Hospitals)
            .FirstOrDefaultAsync(c => c.Id == request.HospitalId, cancellationToken);

        // 2. Handle Not Found
        if (center is null)
        {
            return Result.Failure<CenterDetailResponse>(
                Error.NotFound("Centers.NotFound", $"The center with ID {request.HospitalId} was not found."));
        }

        // 3. Map to Detail Response
        var response = new CenterDetailResponse(
            center.Id,
            center.Name,
            center.CenterType.ToString(),
            center.City,
            center.LocalisationMaps?.x,
            center.LocalisationMaps?.y,
            center.LocalisationMaps?.z,
            center.Hospitals.Select(h => new HospitalSummaryResponse(
                h.Id,
                h.Name,
                h.City,
                h.HospitalType.ToString()
            )).ToList()
        );

        return Result.Success(response);
    }
}
