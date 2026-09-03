using System.Diagnostics;
using System.Text;

namespace PGSH.Infrastructure.Backups;

/// <summary>
/// Runs one executable with one argument list and waits for it. No shell, and — the point —
/// <b>no pipes</b>.
/// </summary>
/// <remarks>
/// ⚠ <b>A piped <c>pg_dump</c> has already corrupted a dump on this project.</b> The archive format is
/// binary and a shell in the middle of it is free to translate line endings or re-encode; the dump is
/// therefore always written with <c>-f</c> to a file inside the container and copied out afterwards,
/// which is what this runner exists to make possible. <c>UseShellExecute = false</c> plus an argument
/// list (never a joined command string) also keeps quoting out of it: a Windows path with a space in
/// it is one argument here and two through a shell.
///
/// <para>stdout and stderr are drained on their own handlers rather than read to the end after
/// waiting. A child that fills a pipe buffer nobody is reading blocks forever, and « the backup
/// hangs » would then be indistinguishable from « the base is large ».</para>
/// </remarks>
internal static class ProcessRunner
{
    public sealed record Execution(int ExitCode, string StandardOutput, string StandardError)
    {
        public bool Succeeded => ExitCode == 0;

        /// <summary>
        /// The last line that says anything, for a refusal message. A caller printing the whole of
        /// stderr onto a toast prints a stack of notices around the one line that matters.
        /// </summary>
        public string Reason
        {
            get
            {
                string text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;

                string? line = text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault(l => l.Length > 0);

                return line ?? $"code de sortie {ExitCode}";
            }
        }
    }

    public static async Task<Execution> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new Execution(-1, stdout.ToString(), $"délai dépassé après {timeout.TotalSeconds:0} s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new Execution(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* Already gone, or gone between the check and the kill. Nothing to report. */ }
    }
}
