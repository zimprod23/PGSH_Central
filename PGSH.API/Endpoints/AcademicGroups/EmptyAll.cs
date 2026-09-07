using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.Empty;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class EmptyAllGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // `levelId` is optional and narrows the act to one promotion — the scope a re-decoupage works
        // at. Omitting it keeps the year-wide act the page's unfiltered button has always sent.
        app.MapDelete("groups/all/students",
            async (int academicYearId, int? levelId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new EmptyAllYearGroupsCommand(academicYearId, levelId), ct);
                return result.Match(count => Results.Ok(new { unassigned = count }), CustomResults.Problem);
            })
            .WithTags(Tags.Groups)
            .RequireAuthorization();
    }
}
