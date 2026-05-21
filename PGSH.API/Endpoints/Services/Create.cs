using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Hospitals.Services.Create;

namespace PGSH.API.Endpoints.Services;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("services", async (CreateServiceCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"/services/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Services);
    }
}
