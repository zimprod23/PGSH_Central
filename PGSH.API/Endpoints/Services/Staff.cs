using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.Chef;
using PGSH.Application.Hospitals.Services.Staff;

namespace PGSH.API.Endpoints.Services;

public sealed class ServiceStaffEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("services/{id:int}/staff", async (
            int id, StaffRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignStaffCommand(id, request.EmployeeId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services)
        .RequireAuthorization();

        app.MapDelete("services/{id:int}/staff/{employeeId:guid}", async (
            int id, Guid employeeId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RemoveStaffCommand(id, employeeId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services)
        .RequireAuthorization();

        app.MapPut("services/{id:int}/chef", async (
            int id, StaffRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignChefCommand(id, request.EmployeeId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services)
        .RequireAuthorization();

        app.MapDelete("services/{id:int}/chef", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RemoveChefCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Services)
        .RequireAuthorization();
    }

    public sealed record StaffRequest(Guid EmployeeId);
}
