using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// The points on disk, newest first.
/// </summary>
/// <remarks>
/// Paginated like every other list, even though retention bounds it: retention is a setting, and a
/// list whose finiteness depends on a setting being right is unbounded. Paged in memory because the
/// archive is a directory rather than a table — the shape of the answer is the same.
/// </remarks>
public sealed record GetBackupPointsQuery(int PageNumber = 1, int PageSize = 25)
    : IQuery<PaginatedResponse<BackupPointResponse>>;

internal sealed class GetBackupPointsQueryHandler(
    IBackupArchive archive,
    ISchemaFingerprintProvider fingerprints,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetBackupPointsQuery, PaginatedResponse<BackupPointResponse>>
{
    public async Task<Result<PaginatedResponse<BackupPointResponse>>> Handle(
        GetBackupPointsQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BackupErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<PaginatedResponse<BackupPointResponse>>(access.Error);

        var probe = await archive.ProbeAsync(cancellationToken);
        if (!probe.Reachable)
            return Result.Failure<PaginatedResponse<BackupPointResponse>>(
                BackupErrors.Unavailable(probe.Reason ?? "raison inconnue"));

        var running = await fingerprints.GetAsync(cancellationToken);
        var points = await archive.ListAsync(cancellationToken);

        int pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        int pageSize = request.PageSize < 1
            ? 25
            : Math.Min(request.PageSize, QueryableExtensions.MaxPageSize);

        var items = points
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(point => point.ToResponse(running))
            .ToList();

        return new PaginatedResponse<BackupPointResponse>(items, pageNumber, pageSize, points.Count);
    }
}
