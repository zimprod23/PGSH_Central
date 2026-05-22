using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Attendance.GetByPeriod;

namespace PGSH.API.Endpoints.Attendance;

public sealed class GetAttendanceByPeriod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("service-periods/{periodId:guid}/attendance", async (
            Guid periodId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAttendanceQuery(periodId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Attendance)
        .RequireAuthorization();
    }
}
