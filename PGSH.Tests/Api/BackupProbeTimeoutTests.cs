using FluentAssertions;
using PGSH.Infrastructure.Backups;
using Xunit;

namespace PGSH.Tests.Api;

/// <summary>
/// Une sonde qui n'a pas répondu <b>est</b> une réponse — et elle ne doit pas coûter le délai d'un
/// <c>pg_dump</c> pour être rendue.
///
/// <para>⚠ <b>Deux durées sans rapport portaient un seul nombre.</b> <c>RunDockerAsync</c> appliquait
/// les 600 s de <c>BackupOptions.TimeoutSeconds</c> au <c>docker version</c> de la sonde comme au
/// dump lui-même. Le 13/09/2026, WSL s'étant mis à jour de lui-même et le moteur Docker s'arrêtant
/// avec lui, l'écran des sauvegardes pouvait donc rester en attente <b>dix minutes</b> — alors que la
/// phrase qui explique la situation existait déjà et n'attendait que de pouvoir être dite.</para>
/// </summary>
public class BackupProbeTimeoutTests
{
    /// <summary>
    /// Le réglage par défaut est ce qui protège : personne ne renseigne <c>ProbeTimeoutSeconds</c>
    /// avant d'avoir vécu la panne.
    /// </summary>
    [Fact]
    public void The_probe_has_its_own_timeout_and_it_is_nothing_like_the_dumps()
    {
        var options = new BackupOptions();

        options.TimeoutSeconds.Should().Be(600, "un dump de la base vivante prend des minutes");
        options.ProbeTimeoutSeconds.Should().BeLessThan(30,
            "un « docker version » répond en une seconde ou ne répondra pas");
    }

    /// <summary>
    /// ⚠ <b>Les deux échecs envoient l'opérateur à des endroits différents</b> : un moteur arrêté se
    /// démarre, un moteur qui n'a pas répondu en dix secondes est en train de démarrer ou de
    /// s'arrêter et il faut attendre. Une phrase unique recouvrirait les deux états.
    /// </summary>
    [Fact]
    public void A_probe_that_timed_out_says_so_rather_than_blaming_a_stopped_engine()
    {
        var timedOut = new ProcessRunner.Execution(
            -1, string.Empty, "délai dépassé après 10 s", TimedOut: true);

        string message = PgDumpBackupArchive.DockerProbeFailure(timedOut, TimeSpan.FromSeconds(10));

        message.Should().Contain("10 s");
        message.Should().NotContain("le moteur est-il démarré",
            "il tourne peut-être très bien : il n'a pas répondu à temps, ce qui est un autre fait");
    }

    /// <summary>Et le moteur qui répond « je ne suis pas là » garde sa phrase, avec ce qu'il a dit.</summary>
    [Fact]
    public void A_probe_the_engine_refused_keeps_the_engines_own_words()
    {
        var refused = new ProcessRunner.Execution(
            1, string.Empty, "Docker Desktop is unable to start");

        string message = PgDumpBackupArchive.DockerProbeFailure(refused, TimeSpan.FromSeconds(10));

        message.Should().Contain("le moteur est-il démarré");
        message.Should().Contain("Docker Desktop is unable to start",
            "la phrase du moteur est la seule qui dise ce qui se passe réellement");
    }

    /// <summary>
    /// Le délai est réellement appliqué — vérifié sur un vrai processus, parce que c'est la seule
    /// façon de savoir que le drapeau est posé par le chemin qui l'observe.
    /// </summary>
    /// <remarks>
    /// ⚠ Windows seulement : la commande lente utilisée ici est <c>ping -n</c>. Ailleurs le test ne
    /// vérifie rien, et le dit plutôt que de prétendre le contraire.
    /// </remarks>
    [Fact]
    public async Task A_command_that_outlives_its_timeout_comes_back_marked_as_timed_out()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var execution = await ProcessRunner.RunAsync(
            "cmd.exe",
            ["/c", "ping", "-n", "6", "127.0.0.1"],
            TimeSpan.FromMilliseconds(400),
            environment: null,
            CancellationToken.None);

        execution.TimedOut.Should().BeTrue();
        execution.Succeeded.Should().BeFalse();
        execution.Reason.Should().Contain("délai dépassé");
    }

    /// <summary>Le témoin : une commande qui répond n'est pas marquée comme expirée.</summary>
    [Fact]
    public async Task A_command_that_answers_in_time_is_not_marked_as_timed_out()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var execution = await ProcessRunner.RunAsync(
            "cmd.exe",
            ["/c", "echo", "pgsh"],
            TimeSpan.FromSeconds(30),
            environment: null,
            CancellationToken.None);

        execution.TimedOut.Should().BeFalse();
        execution.Succeeded.Should().BeTrue();
        execution.StandardOutput.Should().Contain("pgsh");
    }
}
