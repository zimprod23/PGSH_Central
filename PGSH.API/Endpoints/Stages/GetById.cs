
using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.GetById;

namespace PGSH.API.Endpoints.Stages;

public class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/{id:int}", async (int Id, ISender sender, CancellationToken cancellationToken) => {
            var query = new GetStageByIdQuery(Id);
            var result = await sender.Send(query);
            return result.Match(Results.Ok,CustomResults.Problem);
        }).WithTags(Tags.Stages);
    }
}
