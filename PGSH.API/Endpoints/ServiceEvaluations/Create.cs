using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Evaluations.Create;

namespace PGSH.API.Endpoints.ServiceEvaluations;

public sealed class CreateServiceEvaluation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("service-evaluations", async (
            CreateServiceEvaluationCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(
                id => Results.Created($"service-evaluations/{id}", id),
                CustomResults.Problem);
        })
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();
    }
}
