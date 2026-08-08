using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.InternshipAssignments.GetDossier;

namespace PGSH.API.Endpoints.Stages;

public sealed class GetStudentLevelDossier : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("students/{studentId:guid}/levels/{levelId:int}/dossier", async (
            Guid studentId, int levelId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStudentLevelDossierQuery(studentId, levelId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
