using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Backups;
using PGSH.Domain.Backups;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// The safe-point routes through the real pipeline.
///
/// <para>What lives outside the handler here is the whole point of the feature. The banner every bulk
/// act reads is one <c>GET</c>, and it has to answer even when the archive is unreachable — a status
/// endpoint that 500s in the one situation it exists to report is worse than none. The role split is
/// pipeline-shaped too: <b>creating</b> a point must be reachable by scolarité, because scolarité is
/// who applies the déliberation, while <b>deleting</b> one is not.</para>
///
/// <para>⚠ Every refusal here is paired with an assertion that the archive is <em>unchanged</em>, and
/// with a control that still succeeds. A route that refused everything — a typo in the path, a
/// binding failure — would satisfy every refusal assertion and prove nothing.</para>
/// </summary>
public class BackupEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    // The API registers JsonStringEnumConverter globally, so a state travels as "Fresh", not 4.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;

    public BackupEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await _factory.SeedAsync(_ => { });
        _factory.Backups.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Scolarite() => _factory.CreateApiClient(roles: Roles.Scolarite);
    private HttpClient SuperUser() => _factory.CreateApiClient(roles: Roles.SuperUser);
    private HttpClient Chef() => _factory.CreateApiClient(roles: Roles.Professor);

    private static BackupManifest Point(string id, DateTime takenAt, BackupKind kind = BackupKind.Scheduled) =>
        new(id, "Point " + id, kind, takenAt, 2048, new SchemaFingerprint("M1", "sha"),
            DatabaseCensus.Empty, null, null, BackupVerification.Never, null);

    [Fact]
    public async Task An_anonymous_caller_reaches_nothing()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/api/backups/safe-point");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_chef_may_not_read_the_safe_point_status()
    {
        var response = await Chef().GetAsync("/api/backups/safe-point");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_empty_archive_answers_None_rather_than_failing()
    {
        var status = await Scolarite().GetFromJsonAsync<SafePointStatusResponse>("/api/backups/safe-point", Json);

        status!.State.Should().Be(SafePointState.None);
        status.HasUsableUndo.Should().BeFalse();
        status.Latest.Should().BeNull();
        status.TotalPoints.Should().Be(0);
    }

    /// <summary>
    /// ⚠ The case the whole state machine exists for. An unreachable runner must reach the screen as
    /// its own state carrying its own sentence — not as « aucune sauvegarde », and not as a 500 that
    /// leaves the confirmation dialog with nothing to say at all.
    /// </summary>
    [Fact]
    public async Task An_unreachable_archive_still_answers_the_status_and_names_the_reason()
    {
        _factory.Backups.Reachable = false;
        _factory.Backups.UnreachableReason = "Docker ne répond pas";

        var response = await Scolarite().GetAsync("/api/backups/safe-point");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<SafePointStatusResponse>(Json);

        status!.State.Should().Be(SafePointState.Unavailable);
        status.UnavailableReason.Should().Be("Docker ne répond pas");
        status.HasUsableUndo.Should().BeFalse();
    }

    /// <summary>The listing, unlike the status, is allowed to refuse — nobody is mid-act reading it.</summary>
    [Fact]
    public async Task Listing_refuses_when_the_archive_is_unreachable()
    {
        _factory.Backups.Reachable = false;

        var response = await Scolarite().GetAsync("/api/backups");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Scolarite_may_take_a_point_because_scolarite_is_who_applies_the_bulk_acts()
    {
        var response = await Scolarite().PostAsJsonAsync(
            "/api/backups", new { label = "Avant réinscription", kind = "PreAct" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var point = await response.Content.ReadFromJsonAsync<BackupPointResponse>(Json);
        point!.Label.Should().Be("Avant réinscription");
        point.Kind.Should().Be(BackupKind.PreAct);

        _factory.Backups.Points.Should().HaveCount(1);
    }

    [Fact]
    public async Task Taking_a_point_records_who_took_it_and_leaves_an_audit_entry()
    {
        await Scolarite().PostAsJsonAsync("/api/backups", new { label = "Manuel" });

        _factory.Backups.Points.Single().TakenBy.Should().Be("Integration Caller");

        var audited = await _factory.QueryAsync(db =>
            Task.FromResult(db.AuditLogs.Any(a => a.Action == "BACKUP_POINT_CREATED")));

        // ⚠ Taking a dump touches no aggregate, so nothing else in that handler would ever call
        // SaveChanges — the queued audit entry would sit in the change tracker and be dropped.
        audited.Should().BeTrue();
    }

    [Fact]
    public async Task A_chef_may_not_take_a_point_and_nothing_is_written()
    {
        var response = await Chef().PostAsJsonAsync("/api/backups", new { label = "Interdit" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Backups.CreateCalls.Should().Be(0);
        _factory.Backups.Points.Should().BeEmpty();
    }

    [Fact]
    public async Task A_point_without_a_label_is_refused_by_the_validator_before_the_archive_is_touched()
    {
        var response = await Scolarite().PostAsJsonAsync("/api/backups", new { label = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The guard runs before the write, and a handler test cannot tell that from a post-check.
        _factory.Backups.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_failure_from_the_runner_becomes_a_problem_response_carrying_its_own_sentence()
    {
        _factory.Backups.NextCreateFailure = BackupErrors.DumpFailed("no space left on device");

        var response = await Scolarite().PostAsJsonAsync("/api/backups", new { label = "Plein" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("no space left on device");
    }

    [Fact]
    public async Task Verifying_raises_the_recorded_verification()
    {
        _factory.Backups.Seed(Point("p1", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)));

        var response = await Scolarite().PostAsync("/api/backups/p1/verify", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Backups.Points.Single().Verification.Should().Be(BackupVerification.Listed);
    }

    [Fact]
    public async Task Verifying_something_that_is_not_there_is_a_404()
    {
        var response = await Scolarite().PostAsync("/api/backups/absent/verify", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ⚠ Deleting is the one act here restricted to <c>SuperUser</c>. Scolarité must be able to
    /// <em>take</em> a point — that is the button inside its own confirmation dialogs — and must not
    /// be able to remove somebody else's undo.
    /// </summary>
    [Fact]
    public async Task Scolarite_may_not_delete_a_point_and_it_is_still_there_afterwards()
    {
        _factory.Backups.Seed(
            Point("old", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)),
            Point("new", new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)));

        var response = await Scolarite().DeleteAsync("/api/backups/old");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Backups.Points.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_most_recent_point_is_never_deleted_and_the_control_older_one_is()
    {
        _factory.Backups.Seed(
            Point("old", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)),
            Point("newest", new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)));

        var refused = await SuperUser().DeleteAsync("/api/backups/newest");

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.Backups.Points.Should().HaveCount(2);

        // The control: without it, a route that refused everything would satisfy the assertion above.
        var allowed = await SuperUser().DeleteAsync("/api/backups/old");

        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Backups.Points.Single().Id.Should().Be("newest");
    }

    [Fact]
    public async Task The_restore_plan_names_what_the_rollback_would_discard()
    {
        // A point that saw 10 students, against a base that now holds none: the restore would bring
        // 10 back. Both directions are reported, because the second is as often the reason to restore
        // as the first is the reason not to.
        var census = new DatabaseCensus(new Dictionary<string, long> { ["Students"] = 10 });

        _factory.Backups.Seed(new BackupManifest(
            "p1", "Avant tout", BackupKind.Named,
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), 4096,
            new SchemaFingerprint("M1", "sha"), census, null, null, BackupVerification.Never, null));

        var plan = await Scolarite().GetFromJsonAsync<RestorePlanResponse>("/api/backups/p1/restore-plan", Json);

        plan!.ConfirmationPhrase.Should().Be("p1");
        plan.RestoreCommand.Should().Contain("pg_restore");
        plan.TotalRowsRestored.Should().Be(10);
        plan.TotalRowsDiscarded.Should().Be(0);

        plan.Impact.Single(l => l.Table == "Students").AtSafePoint.Should().Be(10);
    }

    /// <summary>
    /// ⚠ A schema mismatch does not fail the read. The refusal has to be able to say <em>which</em>
    /// migration makes the point usable again, and a query that failed could not.
    /// </summary>
    [Fact]
    public async Task A_restore_plan_under_another_schema_is_returned_with_the_step_it_needs()
    {
        _factory.Backups.Seed(Point("p1", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)));

        var plan = await Scolarite().GetFromJsonAsync<RestorePlanResponse>("/api/backups/p1/restore-plan", Json);

        // The in-memory provider has no migrations at all, so the running fingerprint is unknown —
        // which is exactly the "cannot certify" case, and it must not read as agreement.
        plan!.SchemaMatchesRunning.Should().BeFalse();
        plan.SchemaStepCommand.Should().Contain("dotnet ef database update M1");
    }
}
