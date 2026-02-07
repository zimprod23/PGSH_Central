using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Levels.Update;

namespace PGSH.API.Endpoints.Levels
{
    public class Update : IEndpoint
    {
        public sealed record Request(
            string Label,
            int Year,
            int AcademicProgram);
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPut("levels/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
            {
                var command = new UpdateLevelCommand(
                    id,
                    request.Label,
                    request.Year,
                    request.AcademicProgram);

                var result = await sender.Send(command, ct);

                return result.Match(
                    Results.NoContent, // 204 is the standard for successful PUT
                    CustomResults.Problem);
            })
        .WithTags(Tags.Levels);
        }
    }
}
