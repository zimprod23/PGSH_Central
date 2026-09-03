using MediatR;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Backups;
using PGSH.Domain.Backups;

namespace PGSH.API.Endpoints.Backups;

/// <summary>
/// « Y a-t-il un retour en arrière ? » — read by « Sauvegardes » and by every confirmation dialog of
/// an act that cannot be undone.
/// </summary>
public sealed class GetSafePointStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("backups/safe-point",
            async (ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetSafePointStatusQuery(), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("GetSafePointStatus")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}

public sealed class GetBackupPointsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("backups",
            async ([AsParameters] GetBackupPointsQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("GetBackupPoints")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}

public sealed class CreateBackupPointEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("backups",
            async (CreateBackupPointCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return result.Match(
                    point => Results.Created($"/backups/{point.Id}", point),
                    CustomResults.Problem);
            })
            .WithName("CreateBackupPoint")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}

public sealed class VerifyBackupPointEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("backups/{id}/verify",
            async (string id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new VerifyBackupPointCommand(id), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("VerifyBackupPoint")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}

/// <summary>
/// What restoring one point would cost, and the command that does it.
/// </summary>
/// <remarks>
/// ⚠ There is deliberately <b>no endpoint that restores</b>. A process cannot replace the database it
/// is serving from — the restore drops and recreates objects the API holds open — so the plan is
/// returned and an operator runs it with the stack stopped. An endpoint that pretended otherwise
/// would fail halfway through, on a live base, with no way back.
/// </remarks>
public sealed class GetRestorePlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("backups/{id}/restore-plan",
            async (string id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetRestorePlanQuery(id), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithName("GetRestorePlan")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}

public sealed class DeleteBackupPointEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("backups/{id}",
            async (string id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteBackupPointCommand(id), ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithName("DeleteBackupPoint")
            .WithTags(Tags.Backups)
            .RequireAuthorization();
    }
}
