using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Students.GetParcours;

namespace PGSH.API.Endpoints.Students;

public sealed class GetParcours : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("students/{id:guid}/parcours", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStudentParcoursQuery(id), ct);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithName("GetStudentParcours")
        .WithTags(Tags.Students)
        .RequireAuthorization();
    }
}
