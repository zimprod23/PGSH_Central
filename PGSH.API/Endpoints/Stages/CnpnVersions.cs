using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cnpn.GetCnpnVersions;
using PGSH.Application.Stages.Cnpn.Manage;
using PGSH.Application.Stages.Cnpn.Targeting;
using PGSH.Domain.Common.Utils;

namespace PGSH.API.Endpoints.Stages;

/// <summary>
/// The recorded CNPN texts. Every curriculum screen picks one of these instead of an academic year:
/// a requirement set belongs to a text, and from 2026-2027 two texts govern the same year.
/// </summary>
public sealed class GetCnpnVersions : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cnpn-versions", async (
            AcademicProgram? program, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCnpnVersionsQuery(program), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

/// <summary>
/// Recording and correcting the texts themselves. Until these existed a new arrêté could only be
/// added by hand in SQL, which made the whole CNPN feature unusable without a developer.
/// </summary>
public sealed class ManageCnpnVersions : IEndpoint
{
    public sealed record UpdateRequest(
        string Code, string Label, int TotalYears, string? Reference,
        int? AppliesToEntrantsFromAcademicYearId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cnpn-versions", async (
            CreateCnpnVersionCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(id => Results.Created($"cnpn-versions/{id}", id), CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPut("cnpn-versions/{id:int}", async (
            int id, UpdateRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateCnpnVersionCommand(
                id, request.Code, request.Label, request.TotalYears, request.Reference,
                request.AppliesToEntrantsFromAcademicYearId);

            var result = await sender.Send(command, ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // Returns the number of requirement sets the cascade took with it, so the caller can report
        // what was actually removed. Refused outright while any student is stamped with the text.
        app.MapDelete("cnpn-versions/{id:int}", async (
            int id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteCnpnVersionCommand(id), ct);
            return result.Match(
                curriculaRemoved => Results.Ok(new { curriculaRemoved }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        // "1650.25 reprend 2174.18" — every level at once, then edit the years the arrêté changes.
        app.MapPost("cnpn-versions/{id:int}/clone-curricula", async (
            int id, CloneRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CloneCnpnCurriculaCommand(request.FromCnpnVersionId, id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }

    public sealed record CloneRequest(int FromCnpnVersionId);
}

/// <summary>
/// Rattacher une promotion à un CNPN. Two routes, in the order they are meant to be used: post the
/// rule for a dry run, post it again to freeze it. Both take the same body, so what the preview
/// showed is what the apply writes.
/// </summary>
public sealed class CnpnTargeting : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cnpn-versions/{cnpnVersionId:int}/target/preview", async (
            int cnpnVersionId, CnpnTargetCriteria criteria, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewCnpnTargetQuery(cnpnVersionId, criteria), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();

        app.MapPost("cnpn-versions/{cnpnVersionId:int}/target", async (
            int cnpnVersionId, CnpnTargetCriteria criteria, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApplyCnpnTargetCommand(cnpnVersionId, criteria), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
