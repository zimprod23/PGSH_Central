using System.Diagnostics;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// A fact that needs a real PostgreSQL server, and <b>says so</b> when it cannot have one.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Skipped, never silently green.</b> The alternative shapes are both worse: a test that
/// returns early when Docker is absent reports success for work it did not do — one result standing
/// for two states, which is the recurring defect this codebase names explicitly — and a hard failure
/// makes <c>dotnet test</c> red on any machine without Docker, which is how a suite gets disabled
/// wholesale. A skip with a sentence is the only answer that stays true.</para>
///
/// <para>The probe mirrors the one the backup screen already uses: <c>docker version</c> answers in a
/// second or never, so it gets a short timeout of its own rather than sharing one with the work it
/// guards (<c>BackupOptions.ProbeTimeoutSeconds</c>, and <c>PGSH.Tests/Api/BackupProbeTimeoutTests</c>
/// for why the two numbers are separate). It runs <b>once</b> per process, at discovery.</para>
/// </remarks>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!Docker.Available)
            Skip = Docker.Reason;
    }
}

/// <inheritdoc cref="PostgresFactAttribute"/>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (!Docker.Available)
            Skip = Docker.Reason;
    }
}

internal static class Docker
{
    private static readonly Lazy<(bool Available, string Reason)> Probe = new(Run);

    public static bool Available => Probe.Value.Available;
    public static string Reason => Probe.Value.Reason;

    private static (bool, string) Run()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
                return (false, "Docker is required for the PostgreSQL tests and could not be started.");

            // ⚠ « It refused » and « it never answered » are two different sentences, and an engine
            // that is dying answers neither — hence a timeout rather than a bare WaitForExit.
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return (false, "Docker did not answer within 10s — the PostgreSQL tests were skipped.");
            }

            if (process.ExitCode != 0)
                return (false, "Docker is installed but not running — the PostgreSQL tests were skipped.");

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Docker is unavailable, so the PostgreSQL tests were skipped: {ex.Message}");
        }
    }
}
