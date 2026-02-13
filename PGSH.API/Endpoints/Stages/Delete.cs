using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Delete;

namespace PGSH.API.Endpoints.Stages
{
    public class Delete : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("stages/{id:int}", async (int Id, ISender Sender, CancellationToken cancellationToken) =>
            {
                var command = new DeleteStageCommand(Id);
                var result = await Sender.Send(command);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags(Tags.Stages);
        }
    }
}
