using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.DeleteAll;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class DeleteAllGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // `levelId` is optional and narrows the act to one promotion — the scope every other roster
        // act works at. Omitting it keeps the year-wide act the unfiltered button has always sent.
        app.MapDelete("groups/all",
            async (int academicYearId, int? levelId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteAllGroupsCommand(academicYearId, levelId), ct);
                return result.Match(count => Results.Ok(new { deleted = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
