using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicGroups.GetById;

namespace PGSH.API.Endpoints.AcademicGroups;

public sealed class GetGroupById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // [AsParameters] binds Id from the route and the paging/search fields from the query string.
        app.MapGet("groups/{id:int}", async (
            [AsParameters] GetGroupByIdQuery query, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(query, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Groups)
        .RequireAuthorization();
    }
}
