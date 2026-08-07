using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.Fiche;

namespace PGSH.API.Endpoints.InternshipAssignments;

public sealed class GetFicheDeValidation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internship-assignments/{id:guid}/fiche", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFicheDeValidationQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.InternshipAssignments)
        .RequireAuthorization();
    }
}
