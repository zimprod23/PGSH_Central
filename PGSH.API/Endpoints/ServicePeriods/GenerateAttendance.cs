using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Attendance.Generate;

namespace PGSH.API.Endpoints.ServicePeriods;

public sealed class GenerateServicePeriodAttendance : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("service-periods/{id:guid}/generate-attendance", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GenerateAttendanceCommand(id), ct);
            return result.Match(count => Results.Ok(new { generated = count }), CustomResults.Problem);
        })
        .WithTags(Tags.ServicePeriods)
        .RequireAuthorization();
    }
}
