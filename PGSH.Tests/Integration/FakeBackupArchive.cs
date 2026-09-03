using PGSH.Application.Backups;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Tests.Integration;

/// <summary>
/// The archive, in memory.
/// </summary>
/// <remarks>
/// ⚠ The real one shells out to <c>docker</c> and writes a <c>pg_dump</c> of the live base. A test
/// that reached it would be slow, would need Docker running, and — on a developer machine — would be
/// dumping the faculty's data as a side effect of <c>dotnet test</c>. What these tests are for is the
/// half above the port: routing, authentication, the role split between creating and deleting, and
/// that a refusal leaves the archive untouched.
///
/// <para><see cref="Reachable"/> is settable because « le service ne répond pas » and « il n'y a aucune
/// sauvegarde » are two states the whole feature turns on, and only a fake can produce the first one
/// on demand.</para>
/// </remarks>
internal sealed class FakeBackupArchive : IBackupArchive
{
    private readonly List<BackupManifest> _points = [];

    public bool Reachable { get; set; } = true;
    public string? UnreachableReason { get; set; } = "Docker ne répond pas";
    public Error? NextCreateFailure { get; set; }

    public int CreateCalls { get; private set; }

    public IReadOnlyList<BackupManifest> Points => _points;

    public void Reset()
    {
        _points.Clear();
        CreateCalls = 0;
        Reachable = true;
        NextCreateFailure = null;
    }

    public void Seed(params BackupManifest[] points) => _points.AddRange(points);

    public Task<BackupArchiveProbe> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BackupArchiveProbe(
            Reachable, "/fake/backups", Reachable ? null : UnreachableReason));

    public Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BackupManifest>>(
            _points.OrderByDescending(p => p.TakenAtUtc).ToList());

    public Task<BackupManifest?> FindAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_points.FirstOrDefault(p => p.Id == id));

    public Task<Result<BackupManifest>> CreateAsync(BackupRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;

        if (NextCreateFailure is not null)
            return Task.FromResult(Result.Failure<BackupManifest>(NextCreateFailure));

        var manifest = new BackupManifest(
            $"id-{_points.Count + 1}",
            request.Label,
            request.Kind,
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc).AddMinutes(_points.Count),
            1024,
            request.Schema,
            request.Census,
            request.Note,
            request.TakenBy,
            BackupVerification.Never,
            null);

        _points.Add(manifest);
        return Task.FromResult(Result.Success(manifest));
    }

    public Task<Result<BackupManifest>> VerifyAsync(string id, CancellationToken cancellationToken)
    {
        var point = _points.FirstOrDefault(p => p.Id == id);
        if (point is null)
            return Task.FromResult(Result.Failure<BackupManifest>(BackupErrors.NotFound(id)));

        var verified = point.MarkVerified(
            BackupVerification.Listed, new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc));

        _points[_points.IndexOf(point)] = verified;
        return Task.FromResult(Result.Success(verified));
    }

    public Task<Result> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var point = _points.FirstOrDefault(p => p.Id == id);
        if (point is null)
            return Task.FromResult(Result.Failure(BackupErrors.NotFound(id)));

        _points.Remove(point);
        return Task.FromResult(Result.Success());
    }

    public string DescribeRestoreCommand(BackupManifest manifest) =>
        $"docker exec postgres pg_restore /tmp/{manifest.FileName}";
}
