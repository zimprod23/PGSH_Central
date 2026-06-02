using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.MacroPlan;

namespace PGSH.API.Endpoints.Stages;

public sealed class GenerateMacroPlan : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("stages/macro-plan",
            async (GenerateMacroPlanCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Stages)
            .RequireAuthorization();
    }
}
