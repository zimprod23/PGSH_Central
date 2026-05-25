using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Evaluations.Update;

namespace PGSH.API.Endpoints.ServiceEvaluations;

public sealed class UpdateServiceEvaluation : IEndpoint
{
    public sealed record Request(
        decimal TotalScore,
        string? SupervisorComment,
        List<UpdateObjectiveScoreDto> ObjectiveScores);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("service-evaluations/{id:guid}", async (
            Guid id, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateServiceEvaluationCommand(
                id,
                request.TotalScore,
                request.SupervisorComment,
                request.ObjectiveScores);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.ServiceEvaluations)
        .RequireAuthorization();
    }
}
