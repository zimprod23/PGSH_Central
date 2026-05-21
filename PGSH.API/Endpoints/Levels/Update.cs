using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.Update;
using PGSH.Domain.Common.Utils;

namespace PGSH.API.Endpoints.Levels;

public sealed class Update : IEndpoint
{
    public sealed record Request(string? Label, int Year, AcademicProgram AcademicProgram);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("levels/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateLevelCommand(id, request.Label, request.Year, request.AcademicProgram);
            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Levels);
    }
}
