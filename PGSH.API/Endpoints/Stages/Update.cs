
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Update;
using PGSH.Domain.Stages;

namespace PGSH.API.Endpoints.Stages;

public sealed class Update : IEndpoint
{
    // ⚠ Every field the edit form writes back has to be here. A PUT re-states the whole stage, so a
    // field missing from this record is not "left alone" — it arrives at the command as the default
    // and overwrites what was on the row. RotationMode defaults to PerPeriod, so omitting it silently
    // reverted every single-service stage on any save.
    public sealed record Request(
        string Name,
        int Coefficient,
        string? Description,
        int DurationInDays,
        int LevelId,
        List<UpdateStageObjectiveRequest> Objectives,
        StageRotationMode RotationMode);
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
                request.Objectives,
                request.RotationMode);

            var result = await sender.Send(command, ct);

            return result.Match( Results.NoContent,CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
