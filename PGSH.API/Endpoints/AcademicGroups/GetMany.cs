using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.GetMany;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class GetAcademicGroups : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("groups", async (
            int? academicYearId,
            int? levelId,
            Guid? studentId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAcademicGroupsQuery(academicYearId, levelId, studentId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
