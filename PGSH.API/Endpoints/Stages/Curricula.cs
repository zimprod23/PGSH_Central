using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Curricula.Compare;
using PGSH.Application.Stages.Curricula.Copy;
using PGSH.Application.Stages.Curricula.GetCurriculum;
using PGSH.Application.Stages.Curricula.Save;
using PGSH.Application.Stages.Curricula.SeedFromHistory;

namespace PGSH.API.Endpoints.Stages;

public sealed class GetCurriculum : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels/{levelId:int}/curriculum/{cnpnVersionId:int}", async (
            int levelId, int cnpnVersionId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCurriculumQuery(levelId, cnpnVersionId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

public sealed class CompareCurricula : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("levels/{levelId:int}/curriculum/compare", async (
            int levelId, int fromCnpnVersionId, int toCnpnVersionId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CompareCurriculaQuery(levelId, fromCnpnVersionId, toCnpnVersionId), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

public sealed class SaveCurriculum : IEndpoint
{
    public sealed record Request(string? Reference, IReadOnlyList<CurriculumStageInput> Stages);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // PUT, not POST: the whole set for (level, CNPN) is submitted at once, so the call is
        // idempotent — sending the same set twice leaves the same requirements.
        app.MapPut("levels/{levelId:int}/curriculum/{cnpnVersionId:int}", async (
            int levelId, int cnpnVersionId, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new SaveCurriculumCommand(
                levelId, cnpnVersionId, request.Reference, request.Stages ?? []);

            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

public sealed class CopyCurriculum : IEndpoint
{
    public sealed record Request(int FromCnpnVersionId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("levels/{levelId:int}/curriculum/{cnpnVersionId:int}/copy", async (
            int levelId, int cnpnVersionId, Request request, ISender sender, CancellationToken ct) =>
        {
            var command = new CopyCurriculumCommand(levelId, request.FromCnpnVersionId, cnpnVersionId);
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}

public sealed class SeedCurriculaFromHistory : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Dry run by default, like the evaluation import: the caller sees the plan before it lands.
        app.MapPost("curricula/seed-from-history", async (
            SeedCurriculaFromHistoryCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Stages)
        .RequireAuthorization();
    }
}
