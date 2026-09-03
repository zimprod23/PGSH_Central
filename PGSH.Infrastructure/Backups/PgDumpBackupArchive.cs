using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PGSH.Application.Backups;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Infrastructure.Backups;

/// <summary>
/// The archive, implemented with the procedure this project has actually used successfully three
/// times: <c>docker exec … pg_dump -Fc -f</c> inside the container, then <c>docker cp</c> out.
/// </summary>
/// <remarks>
/// ⚠ <b>Never piped.</b> <c>SMOKE-TEST.md</c> records a dump corrupted by piping it out of the
/// container, and the archive format is binary — see <see cref="ProcessRunner"/>.
///
/// <para>⚠ <b>The container, not the host.</b> <c>pg_dump</c> is not on a Windows developer machine by
/// default and, worse, a <em>mismatched</em> one refuses the server's archive version — while the
/// Postgres container is guaranteed to carry the exact tools for the server it is running. Docker is
/// present by construction: it is what the base is running in.</para>
///
/// <para>⚠ <b>The dump never touches <c>pgsh-postgres-data</c>.</b> A backup written into the volume it
/// backs up is not a backup; the file is copied out to <see cref="BackupOptions.Directory"/>, which
/// defaults outside the repository as well — the repository is not a safe place for a file naming
/// 10 203 real students.</para>
/// </remarks>
internal sealed class PgDumpBackupArchive(
    IOptions<BackupOptions> options,
    IConfiguration configuration,
    IDateTimeProvider clock,
    ILogger<PgDumpBackupArchive> logger)
    : IBackupArchive
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// How long a successful probe is trusted. ⚠ Every bulk-act confirmation reads the safe-point
    /// status, so an uncached probe would shell out to <c>docker ps</c> each time a dialog opens.
    /// Short enough that a container going away is noticed within the minute.
    /// </summary>
    private static readonly TimeSpan ProbeCacheFor = TimeSpan.FromSeconds(30);

    private readonly BackupOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private BackupArchiveProbe? _cachedProbe;
    private DateTime _probedAtUtc = DateTime.MinValue;
    private string? _containerName;

    public string Directory => string.IsNullOrWhiteSpace(_options.Directory)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PGSH",
            "backups")
        : _options.Directory;

    public async Task<BackupArchiveProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        if (_cachedProbe is not null && clock.UtcNow - _probedAtUtc < ProbeCacheFor)
            return _cachedProbe;

        var probe = await MeasureAsync(cancellationToken);

        _cachedProbe = probe;
        _probedAtUtc = clock.UtcNow;
        return probe;
    }

    private async Task<BackupArchiveProbe> MeasureAsync(CancellationToken cancellationToken)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex)
        {
            return new BackupArchiveProbe(false, Directory, $"dossier inaccessible ({ex.Message})");
        }

        var docker = await RunDockerAsync(["version", "--format", "{{.Server.Version}}"], cancellationToken);
        if (!docker.Succeeded)
            return new BackupArchiveProbe(false, Directory, "Docker ne répond pas (le moteur est-il démarré ?)");

        if (!string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            _containerName = _options.ContainerName;
            return new BackupArchiveProbe(true, Directory, null);
        }

        var candidates = await DiscoverContainersAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new BackupArchiveProbe(
                false,
                Directory,
                "aucun conteneur PostgreSQL en cours d'exécution n'a été trouvé "
                + "(renseignez Backups:ContainerName si l'image ne s'appelle pas « postgres »)");
        }

        // ⚠ Refused rather than resolved by picking the first. A developer machine routinely runs
        // several PostgreSQL containers, and a dump of the *wrong* database — filed and labelled as a
        // safe point for this one — is the silent failure this whole phase exists to remove. Naming
        // them is what makes the setting obvious.
        if (candidates.Count > 1)
        {
            return new BackupArchiveProbe(
                false,
                Directory,
                $"plusieurs conteneurs PostgreSQL tournent ({string.Join(", ", candidates)}) : "
                + "renseignez Backups:ContainerName pour dire lequel héberge PGSH");
        }

        _containerName = candidates[0];
        return new BackupArchiveProbe(true, Directory, null);
    }

    /// <summary>
    /// ⚠ Discovered rather than configured, because Aspire names the container itself
    /// (<c>postgres-<em>suffix</em></c>) and a name written into settings goes stale on the next
    /// <c>dotnet run</c>. pgAdmin is excluded by image: it sits in the same compose and its name
    /// contains « postgres » too.
    /// </summary>
    private async Task<List<string>> DiscoverContainersAsync(CancellationToken cancellationToken)
    {
        var listed = await RunDockerAsync(
            ["ps", "--filter", "status=running", "--format", "{{.Names}}\t{{.Image}}"],
            cancellationToken);

        if (!listed.Succeeded)
            return [];

        return listed.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 2)
            .Where(parts => IsPostgresImage(parts[1]))
            .Select(parts => parts[0])
            .ToList();
    }

    private static bool IsPostgresImage(string image)
    {
        string name = image.Split('/').Last();
        return name.StartsWith("postgres", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var manifests = new List<BackupManifest>();

        foreach (string path in System.IO.Directory.EnumerateFiles(
                     Directory, "*" + BackupManifest.ManifestExtension))
        {
            var manifest = await ReadManifestAsync(path, cancellationToken);
            if (manifest is null)
                continue;

            // A manifest whose dump has been removed by hand describes a point that cannot be
            // restored. Listing it would put an undo on screen that does not exist.
            if (!File.Exists(Path.Combine(Directory, manifest.FileName)))
            {
                logger.LogWarning(
                    "Manifeste {Id} sans archive : ignoré.", manifest.Id);
                continue;
            }

            manifests.Add(manifest);
        }

        return manifests.OrderByDescending(m => m.TakenAtUtc).ToList();
    }

    public async Task<BackupManifest?> FindAsync(string id, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Directory, SafeId(id) + BackupManifest.ManifestExtension);
        if (!File.Exists(path))
            return null;

        var manifest = await ReadManifestAsync(path, cancellationToken);
        return manifest is not null && File.Exists(Path.Combine(Directory, manifest.FileName))
            ? manifest
            : null;
    }

    public async Task<Result<BackupManifest>> CreateAsync(
        BackupRequest request, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(cancellationToken);
        if (!probe.Reachable)
            return Result.Failure<BackupManifest>(BackupErrors.Unavailable(probe.Reason ?? "raison inconnue"));

        // One dump at a time. Two concurrent pg_dumps of a 100 000-période base is load nobody asked
        // for, and the second one is nearly always the timer landing on top of a human's click.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var takenAt = clock.UtcNow;
            string id = $"{takenAt:yyyyMMdd-HHmmss}-{Slug(request.Label)}";
            string containerPath = $"/tmp/{id}{BackupManifest.DumpExtension}";
            string localPath = Path.Combine(Directory, id + BackupManifest.DumpExtension);

            var connection = ReadConnection();

            var dump = await RunDockerAsync(
                [
                    "exec",
                    "-e", "PGPASSWORD=" + connection.Password,
                    _containerName!,
                    "pg_dump",
                    "-U", connection.Username,
                    "-d", connection.Database,
                    "-Fc",
                    "-f", containerPath,
                ],
                cancellationToken);

            if (!dump.Succeeded)
                return Result.Failure<BackupManifest>(BackupErrors.DumpFailed(dump.Reason));

            var copy = await RunDockerAsync(
                ["cp", $"{_containerName}:{containerPath}", localPath],
                cancellationToken);

            await RunDockerAsync(["exec", _containerName!, "rm", "-f", containerPath], cancellationToken);

            if (!copy.Succeeded)
                return Result.Failure<BackupManifest>(BackupErrors.DumpFailed(copy.Reason));

            var manifest = new BackupManifest(
                id,
                request.Label,
                request.Kind,
                takenAt,
                new FileInfo(localPath).Length,
                request.Schema,
                request.Census,
                request.Note,
                request.TakenBy,
                BackupVerification.Never,
                null);

            await WriteManifestAsync(manifest, cancellationToken);

            logger.LogInformation(
                "Point de sauvegarde {Id} écrit ({Size} octets) dans {Directory}.",
                manifest.Id, manifest.SizeBytes, Directory);

            return manifest;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<BackupManifest>> VerifyAsync(string id, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(cancellationToken);
        if (!probe.Reachable)
            return Result.Failure<BackupManifest>(BackupErrors.Unavailable(probe.Reason ?? "raison inconnue"));

        var manifest = await FindAsync(id, cancellationToken);
        if (manifest is null)
            return Result.Failure<BackupManifest>(BackupErrors.NotFound(id));

        string localPath = Path.Combine(Directory, manifest.FileName);
        string containerPath = $"/tmp/verify-{manifest.FileName}";

        var copyIn = await RunDockerAsync(
            ["cp", localPath, $"{_containerName}:{containerPath}"], cancellationToken);

        if (!copyIn.Succeeded)
            return Result.Failure<BackupManifest>(
                BackupErrors.VerificationFailed(manifest.Id, copyIn.Reason));

        // -l lists the archive's table of contents without touching a database. It reads the whole
        // file, which is exactly what catches a truncation the file's mere existence does not.
        var listed = await RunDockerAsync(
            ["exec", _containerName!, "pg_restore", "-l", containerPath], cancellationToken);

        await RunDockerAsync(["exec", _containerName!, "rm", "-f", containerPath], cancellationToken);

        if (!listed.Succeeded || string.IsNullOrWhiteSpace(listed.StandardOutput))
            return Result.Failure<BackupManifest>(
                BackupErrors.VerificationFailed(manifest.Id, listed.Reason));

        var verified = manifest.MarkVerified(BackupVerification.Listed, clock.UtcNow);
        await WriteManifestAsync(verified, cancellationToken);

        return verified;
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var manifest = await FindAsync(id, cancellationToken);
        if (manifest is null)
            return Result.Failure(BackupErrors.NotFound(id));

        try
        {
            File.Delete(Path.Combine(Directory, manifest.FileName));
            File.Delete(Path.Combine(Directory, manifest.ManifestFileName));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Problem(
                "Backups.DeleteFailed",
                $"Le point « {manifest.Id} » n'a pas pu être supprimé : {ex.Message}"));
        }

        return Result.Success();
    }

    /// <summary>
    /// The command an operator runs, with the stack stopped.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The password is a placeholder, never the value.</b> This string is rendered on a web page,
    /// and a credential on a screen is a credential in a screenshot. It cannot simply be omitted
    /// either: measured 2026-09-03 against the running container, the image's local socket is
    /// <c>scram-sha-256</c>, not <c>trust</c> — a command without <c>PGPASSWORD</c> fails with
    /// « no password supplied », which reads as a broken instruction rather than as a missing value.
    /// So the line that <em>obtains</em> it is printed instead.
    /// </remarks>
    public string DescribeRestoreCommand(BackupManifest manifest)
    {
        var connection = ReadConnection();
        string container = _containerName ?? "<conteneur postgres>";
        string localPath = Path.Combine(Directory, manifest.FileName);

        return string.Join(
            Environment.NewLine,
            "# 1. Arrêter l'AppHost : une restauration ne peut pas remplacer une base en cours d'utilisation.",
            "# 2. Relever le mot de passe (il n'est pas affiché ici) :",
            $"docker exec {container} printenv POSTGRES_PASSWORD",
            "# 3. Restaurer :",
            $"docker cp \"{localPath}\" {container}:/tmp/{manifest.FileName}",
            $"docker exec -e PGPASSWORD=<mot de passe> {container} pg_restore "
            + $"-U {connection.Username} -d {connection.Database} "
            + $"--clean --if-exists --no-owner /tmp/{manifest.FileName}",
            $"docker exec {container} rm -f /tmp/{manifest.FileName}",
            "# 4. Relancer l'AppHost et vérifier les effectifs annoncés par ce point.");
    }

    private (string Username, string Password, string Database) ReadConnection()
    {
        string raw = configuration.GetConnectionString("TodoDatabase") ?? string.Empty;
        var builder = new NpgsqlConnectionStringBuilder(raw);

        return (
            builder.Username ?? "postgres",
            builder.Password ?? string.Empty,
            builder.Database ?? "postgres");
    }

    private Task<ProcessRunner.Execution> RunDockerAsync(
        IEnumerable<string> arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(
            _options.DockerPath,
            arguments,
            TimeSpan.FromSeconds(_options.TimeoutSeconds),
            environment: null,
            cancellationToken);

    private async Task<BackupManifest?> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<ManifestDocument>(
                stream, ManifestJson, cancellationToken);

            return document?.ToManifest();
        }
        catch (Exception ex)
        {
            // Skipped rather than thrown: one unreadable file must not take the whole list — and with
            // it every other undo — off the screen.
            logger.LogWarning(ex, "Manifeste illisible : {Path}", path);
            return null;
        }
    }

    private async Task WriteManifestAsync(BackupManifest manifest, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Directory, manifest.ManifestFileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream, ManifestDocument.From(manifest), ManifestJson, cancellationToken);
    }

    /// <summary>A label reduced to something usable as a file name and typed at a terminal.</summary>
    private static string Slug(string label)
    {
        var builder = new StringBuilder();

        foreach (char c in label.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');

        return slug.Length == 0 ? "point" : slug;
    }

    /// <summary>
    /// ⚠ An id arrives from a route. Anything but the shape this class writes is refused rather than
    /// combined into a path — <c>Path.Combine</c> with « ../../ » leaves the archive directory.
    /// </summary>
    private static string SafeId(string id) =>
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-') ? id : "invalide";

    /// <summary>
    /// The on-disk shape, kept apart from the domain record so the file stays readable by a build that
    /// has since changed the type — a manifest is a document, and it outlives the code that wrote it.
    /// </summary>
    private sealed record ManifestDocument(
        string Id,
        string Label,
        BackupKind Kind,
        DateTime TakenAtUtc,
        long SizeBytes,
        string? LastMigration,
        string? GitSha,
        Dictionary<string, long> Census,
        string? Note,
        string? TakenBy,
        BackupVerification Verification,
        DateTime? VerifiedAtUtc)
    {
        public static ManifestDocument From(BackupManifest manifest) =>
            new(
                manifest.Id,
                manifest.Label,
                manifest.Kind,
                manifest.TakenAtUtc,
                manifest.SizeBytes,
                manifest.Schema.LastMigration,
                manifest.Schema.GitSha,
                new Dictionary<string, long>(manifest.Census.Counts),
                manifest.Note,
                manifest.TakenBy,
                manifest.Verification,
                manifest.VerifiedAtUtc);

        public BackupManifest ToManifest() =>
            new(
                Id,
                Label,
                Kind,
                DateTime.SpecifyKind(TakenAtUtc, DateTimeKind.Utc),
                SizeBytes,
                new SchemaFingerprint(LastMigration, GitSha),
                new DatabaseCensus(Census ?? []),
                Note,
                TakenBy,
                Verification,
                VerifiedAtUtc);
    }
}
