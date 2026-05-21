using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.AcademicYears.Create;

namespace PGSH.API.Endpoints.AcademicYears;

public sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("academic-years", async (CreateAcademicYearCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"/academic-years/{id}", id), CustomResults.Problem);
        })
        .WithTags("AcademicYears")
        .WithName("CreateAcademicYear");
    }
}
