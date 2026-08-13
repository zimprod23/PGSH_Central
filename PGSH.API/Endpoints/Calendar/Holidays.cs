using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;

namespace PGSH.API.Endpoints.Calendar;

public sealed class GetHolidayCoverageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("calendar/holidays",
            async ([AsParameters] GetHolidayCoverageQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("GetHolidayCoverage")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class CreateHolidayEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("calendar/holidays",
            async (CreateHolidayCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return result.Match(id => Results.Created($"/calendar/holidays/{id}", id),
                    CustomResults.Problem);
            })
            .WithName("CreateHoliday")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class UpdateHolidayEndpoint : IEndpoint
{
    public sealed record Request(
        DateOnly StartDate,
        DateOnly EndDate,
        string Name,
        HolidayKind Kind,
        bool IsConfirmed);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("calendar/holidays/{id:int}",
            async (int id, Request request, ISender sender, CancellationToken ct) =>
            {
                var command = new UpdateHolidayCommand(
                    id, request.StartDate, request.EndDate, request.Name, request.Kind,
                    request.IsConfirmed);

                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("UpdateHoliday")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class DeleteHolidayEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("calendar/holidays/{id:int}",
            async (int id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteHolidayCommand(id), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("DeleteHoliday")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}

public sealed class SeedNationalHolidaysEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("calendar/holidays/seed-national",
            async (int? academicYearId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new SeedNationalHolidaysCommand(academicYearId), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("SeedNationalHolidays")
            .WithTags(Tags.Calendar)
            .RequireAuthorization();
    }
}
