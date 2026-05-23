using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Bulk;
using PGSH.Application.Stages.Cohorts.PublishSchedule;
using PGSH.Application.Stages.Cohorts.UnpublishSchedule;

namespace PGSH.API.Endpoints.Cohorts;

public sealed class CohortBulkActions : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cohorts/{id:int}/publish-schedule", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PublishCohortScheduleCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();

        app.MapDelete("cohorts/{id:int}/publish-schedule", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnpublishCohortScheduleCommand(id), ct);
            return result.Match(count => Results.Ok(new { removed = count }), CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();

        app.MapPost("cohorts/{id:int}/start-assignments", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new StartCohortAssignmentsCommand(id), ct);
            return result.Match(count => Results.Ok(new { started = count }), CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();

        app.MapPost("cohorts/{id:int}/complete-periods", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CompletePeriodsCommand(id), ct);
            return result.Match(count => Results.Ok(new { completed = count }), CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();

        app.MapPost("cohorts/{id:int}/validate-assignments", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ValidateCohortAssignmentsCommand(id), ct);
            return result.Match(count => Results.Ok(new { validated = count }), CustomResults.Problem);
        })
        .WithTags(Tags.Cohorts)
        .RequireAuthorization();
    }
}
