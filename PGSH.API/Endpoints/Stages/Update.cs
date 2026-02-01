
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Update;

namespace PGSH.API.Endpoints.Stages;

public sealed class Update : IEndpoint
{
    public sealed record Request(
        string Name,
        int Coefficient,
        string? Description,
        int DurationInDays,
        int LevelId,
        List<UpdateStageObjectiveRequest> Objectives);
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("stages/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateStageCommand(
                id,
                request.Name,
                request.Coefficient,
                request.Description,
                request.DurationInDays,
                request.LevelId,
                request.Objectives);

            var result = await sender.Send(command, ct);

            return result.Match( Results.NoContent,CustomResults.Problem);
        })
        .WithTags(Tags.Stages);
    }
}
