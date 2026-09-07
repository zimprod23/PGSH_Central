using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Calendar.Pauses;
using PGSH.Domain.Stages;

namespace PGSH.API.Endpoints.Calendar;

public sealed class GetPromotionPausesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("calendar/promotion-pauses",
            async ([AsParameters] GetPromotionPausesQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("GetPromotionPauses")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

/// <summary>
/// The dry run. Writes nothing, and returns the numbers the declaration will report — same reader.
/// </summary>
public sealed class PreviewPromotionPauseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("calendar/promotion-pauses/preview",
            async (PreviewPromotionPauseQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("PreviewPromotionPause")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class DeclarePromotionPauseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("calendar/promotion-pauses",
            async (DeclarePromotionPauseCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return result.Match(
                    declared => Results.Created($"/calendar/promotion-pauses/{declared.Id}", declared),
                    CustomResults.Problem);
            })
            .WithName("DeclarePromotionPause")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class CorrectPromotionPauseEndpoint : IEndpoint
{
    public sealed record Request(
        DateOnly StartDate,
        DateOnly EndDate,
        PauseKind Kind,
        string Reason,
        bool IsConfirmed);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("calendar/promotion-pauses/{id:int}",
            async (int id, Request request, ISender sender, CancellationToken ct) =>
            {
                var command = new CorrectPromotionPauseCommand(
                    id, request.StartDate, request.EndDate, request.Kind, request.Reason,
                    request.IsConfirmed);

                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("CorrectPromotionPause")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class RevokePromotionPauseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("calendar/promotion-pauses/{id:int}",
            async (int id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new RevokePromotionPauseCommand(id), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("RevokePromotionPause")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}
