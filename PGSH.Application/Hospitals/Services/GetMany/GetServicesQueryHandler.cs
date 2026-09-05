using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.GetMany;

internal sealed class GetServicesQueryHandler(
    IApplicationDbContext dbContext,
    ServiceChefProvider chefProvider)
    : IQueryHandler<GetServicesQuery, PaginatedResponse<ServiceSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<ServiceSummaryResponse>>> Handle(
        GetServicesQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Service> query = dbContext.Services.AsNoTracking();

        if (request.HospitalId.HasValue)
            query = query.Where(s => s.HospitalId == request.HospitalId.Value);

        if (request.ServiceType.HasValue)
            query = query.Where(s => s.ServiceType == request.ServiceType.Value);

        if (request.ServiceChefId.HasValue)
            query = query.Where(s => s.ServiceChefId == request.ServiceChefId.Value);

        // An unrestricted service admits everyone, so it belongs in the answer too — filtering on
        // the quota row alone would hide every service nobody has restricted yet.
        if (request.AdmitsLevelId.HasValue)
            query = query.Where(s => s.LevelCapacities.Count == 0
                                  || s.LevelCapacities.Any(c => c.LevelId == request.AdmitsLevelId.Value));

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        var page = await query
            .OrderBy(s => s.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new ServiceRow(
                    s.Id, s.Name, s.ServiceType.ToString(), s.Specialty, s.Capacity,
                    s.LevelCapacities.Count,
                    s.HospitalId, s.Hospital.Name,
                    s.ServiceChef != null ? s.ServiceChef.FirstName + " " + s.ServiceChef.LastName : null),
                cancellationToken);

        // Bounded by the page, never by the filter: a directory built over every matching service
        // would grow with the catalogue for rows nobody is looking at. Same rule as the planning
        // grid, which reads its published cells from the ids it just returned.
        var chefs = await chefProvider.BuildAsync(
            page.Items.Select(i => i.Id).ToList(), ServiceChefPolicy.InForce, cancellationToken);

        // Today, because this list answers « qui dirige ce service ? ». A document asking « qui le
        // dirigeait quand ceci a été publié ? » passes its own date — that is why the directory takes
        // one per question rather than being built for a date.
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = page.Items
            .Select(i => new ServiceSummaryResponse(
                i.Id, i.Name, i.ServiceType, i.Specialty, i.Capacity, i.RestrictedLevelCount,
                i.HospitalId, i.HospitalName, i.ServiceChefName,
                // Staff is a field not a property; EF cannot translate field navigation in LINQ-to-SQL
                StaffCount: 0,
                ServiceChefAttributionResponse.From(chefs, i.Id, asOf)))
            .ToList();

        return Result.Success(new PaginatedResponse<ServiceSummaryResponse>(
            items, page.PageNumber, page.PageSize, page.TotalCount));
    }

    /// <summary>
    /// One row as the store answers it, before the chef is resolved.
    /// </summary>
    /// <remarks>
    /// The attribution cannot be part of the projection — it is decided by
    /// <see cref="ServiceChefDirectory"/>, in memory, from three sources one of which is free text —
    /// so the row is materialised first and completed after, the shape
    /// <c>GetStageScheduleQueryHandler.CohortRow</c> established. Projecting straight into
    /// <see cref="ServiceSummaryResponse"/> would mean handing it a null attribution and filling it
    /// in afterwards, i.e. a response type able to exist in a state it must never be sent in.
    /// </remarks>
    internal sealed record ServiceRow(
        int Id,
        string Name,
        string ServiceType,
        string? Specialty,
        int Capacity,
        int RestrictedLevelCount,
        int HospitalId,
        string HospitalName,
        string? ServiceChefName);
}
