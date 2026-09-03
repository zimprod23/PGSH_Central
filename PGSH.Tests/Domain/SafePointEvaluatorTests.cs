using FluentAssertions;
using PGSH.Domain.Backups;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// « Y a-t-il un retour en arrière ? », exhaustively.
///
/// <para>This is the sentence shown on the confirmation of every act that writes a promotion and
/// cannot be undone, so the cases are worth stating exactly. The two that matter most are the ones a
/// single boolean would have collapsed: <b>« le service ne répond pas » is not « il n'y a aucune
/// sauvegarde »</b>, and <b>a point taken under another schema is not an undo</b> — restoring it
/// refuses until somebody runs a migration, which is not something an operator can discover from a
/// green banner.</para>
/// </summary>
public class SafePointEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SchemaFingerprint Running = new("20260830143914_PriorEnrolment", "abc1234");

    private static BackupManifest Point(
        DateTime takenAt,
        string? migration = "20260830143914_PriorEnrolment",
        BackupKind kind = BackupKind.Scheduled) =>
        new(
            "20260903-100000-auto",
            "Sauvegarde automatique",
            kind,
            takenAt,
            1024,
            new SchemaFingerprint(migration, "abc1234"),
            DatabaseCensus.Empty,
            null,
            null,
            BackupVerification.Never,
            null);

    [Fact]
    public void An_unreachable_archive_is_not_an_empty_one()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: false, latest: Point(Now), Running, Now);

        verdict.State.Should().Be(SafePointState.Unavailable);
        verdict.HasUsableUndo.Should().BeFalse();

        // Even holding a perfectly good point: if the runner cannot be reached, nothing is being
        // backed up from here on, and that is the fact the screen has to show.
        verdict.Point.Should().BeNull();
    }

    [Fact]
    public void An_empty_archive_says_so_rather_than_reading_as_unavailable()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, latest: null, Running, Now);

        verdict.State.Should().Be(SafePointState.None);
        verdict.HasUsableUndo.Should().BeFalse();
    }

    [Fact]
    public void A_recent_point_under_the_running_schema_is_fresh()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, Point(Now.AddMinutes(-12)), Running, Now);

        verdict.State.Should().Be(SafePointState.Fresh);
        verdict.Age.Should().Be(TimeSpan.FromMinutes(12));
        verdict.HasUsableUndo.Should().BeTrue();
    }

    [Fact]
    public void A_point_older_than_the_window_is_stale_and_still_an_undo()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, Point(Now.AddDays(-3)), Running, Now);

        verdict.State.Should().Be(SafePointState.Stale);

        // Three days old is a cost, not an obstacle: the restore works and loses three days. That is
        // why the state is distinct from SchemaChanged, which does not work at all.
        verdict.HasUsableUndo.Should().BeTrue();
    }

    [Fact]
    public void A_migration_since_the_point_outranks_its_freshness()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true,
            Point(Now.AddMinutes(-5), migration: "20260701120000_Earlier"),
            Running,
            Now);

        verdict.State.Should().Be(SafePointState.SchemaChanged);
        verdict.HasUsableUndo.Should().BeFalse();
    }

    [Fact]
    public void A_point_that_does_not_know_its_migration_cannot_certify_a_match()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, Point(Now.AddMinutes(-5), migration: null), Running, Now);

        // Unknown is the absence of the evidence a match is made of, never agreement — reading it as
        // "matches" is what would let a dump of unknown provenance be offered as a working undo.
        verdict.State.Should().Be(SafePointState.SchemaChanged);
    }

    [Fact]
    public void A_running_schema_that_cannot_be_read_certifies_nothing_either()
    {
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, Point(Now.AddMinutes(-5)), SchemaFingerprint.Unknown, Now);

        verdict.State.Should().Be(SafePointState.SchemaChanged);
    }

    [Fact]
    public void A_point_stamped_in_the_future_reads_as_age_zero_rather_than_as_negative()
    {
        // Two clocks disagreeing — the API host and whatever wrote the manifest. Unclamped, a negative
        // age passes every freshness comparison by arithmetic accident.
        var verdict = SafePointEvaluator.Evaluate(
            archiveReachable: true, Point(Now.AddHours(2)), Running, Now);

        verdict.Age.Should().Be(TimeSpan.Zero);
        verdict.State.Should().Be(SafePointState.Fresh);
    }

    [Fact]
    public void The_freshness_window_is_the_callers_to_state()
    {
        var point = Point(Now.AddHours(-2));

        SafePointEvaluator.Evaluate(true, point, Running, Now, TimeSpan.FromHours(3))
            .State.Should().Be(SafePointState.Fresh);

        SafePointEvaluator.Evaluate(true, point, Running, Now, TimeSpan.FromHours(1))
            .State.Should().Be(SafePointState.Stale);
    }

    [Fact]
    public void Exactly_at_the_window_is_still_fresh()
    {
        SafePointEvaluator
            .Evaluate(true, Point(Now - SafePointEvaluator.DefaultFreshFor), Running, Now)
            .State.Should().Be(SafePointState.Fresh);
    }
}
