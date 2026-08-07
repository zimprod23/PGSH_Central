using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicYears.GetMany;

namespace PGSH.API.Endpoints.AcademicYears;

public sealed class GetMany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("academic-years", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAcademicYearsQuery(), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags("AcademicYears")
        .WithName("GetAcademicYears")
        .RequireAuthorization();
    }
}
