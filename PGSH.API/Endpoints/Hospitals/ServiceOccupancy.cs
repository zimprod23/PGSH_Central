using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.SharedKernel;
using PGSH.Application.Hospitals.Services.Occupancy;

namespace PGSH.API.Endpoints.Hospitals;

/// <summary>
/// What a service actually holds, who may send students to it, and who is in it — the three reads
/// behind the service detail page. Kept as three endpoints rather than one fat response: they have
/// different shapes and different lifetimes (the timeline changes with the navbar year, the allowed
/// stages do not), and the occupants list is paged.
/// </summary>
public sealed class ServiceOccupancy : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("services/{id:int}/occupancy", async (
            int id, int? academicYearId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetServiceOccupancyQuery(id, academicYearId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital)
        .RequireAuthorization();

        app.MapGet("services/{id:int}/stages", async (int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetServiceStagesQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital)
        .RequireAuthorization();

        app.MapGet("services/{id:int}/occupants", async (
            int id, [AsParameters] OccupantsRequest request, ISender sender, CancellationToken ct) =>
        {
            if (request.StartDate is not { } from || request.EndDate is not { } to)
                return CustomResults.Problem(
                    Result.Failure<int>(ServiceOccupancyErrors.WindowRequired));

            var query = new GetServiceOccupantsQuery(
                id, from, to, request.LevelId, request.StageId,
                request.PageNumber ?? 1, request.PageSize ?? 25, request.SearchTerm);

            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Hospital)
        .RequireAuthorization();
    }

    /// <summary>
    /// The window comes from the caller because a timeline segment is cut at window boundaries and
    /// generally coincides with no single <c>StageSlot</c> — there is no period id to pass instead.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The two dates are nullable here and refused below, not by the model binder.</b> A
    /// non-nullable value type bound from the query string cannot be omitted: ASP.NET throws
    /// <c>BadHttpRequestException</c> inside routing, so the caller gets a bare 400 with nothing the
    /// client can show — and the process pauses for anyone on a debugger. Measured twice on the
    /// running application, 13/09/2026.
    /// </remarks>
    public sealed record OccupantsRequest(
        DateOnly? StartDate,
        DateOnly? EndDate,
        int?     LevelId,
        int?     StageId,
        int?     PageNumber,
        int?     PageSize,
        string?  SearchTerm);
}
