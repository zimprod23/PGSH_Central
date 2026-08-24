using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The CNPN effectivity routes through the real pipeline.
///
/// <para>What lives outside the handler here is the <b>confirmed count</b>. It arrives in the apply's
/// body, and if it failed to bind it would arrive as 0 — which the handler cannot distinguish from an
/// operator who was genuinely shown zero. A handler test constructs the command directly and can
/// never see that. So the apply is tested twice: once with the right number, which must succeed, and
/// once with a stale one, which must refuse <i>and leave the store untouched</i>.</para>
/// </summary>
public class CnpnEffectivityEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int Level3 = 3;
    private const int Level4 = 4;
    private const int OldText = 91;
    private const int NewText = 92;
    private const int RuleId = 1;

    private const string RepeaterCne = "R13089613";

    private readonly ApiFactory _factory;

    public CnpnEffectivityEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One year, two promotions, two texts, and one student sitting in the 3ᵉ année under the old one
    /// — the population an effectivity rule for the 3ᵉ année would move.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId, Label = "2025-2026", IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        });

        db.Levels.Add(new Level
        {
            Id = Level3, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });
        db.Levels.Add(new Level
        {
            Id = Level4, Label = "Quatrième Année Médecine", Year = 4,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.CnpnVersions.Add(new CnpnVersion
        {
            Id = OldText, Code = "2174.18", Label = "CNPN 2019 (7 ans)",
            AcademicProgram = AcademicProgram.Medecine, TotalYears = 7,
        });
        db.CnpnVersions.Add(new CnpnVersion
        {
            Id = NewText, Code = "1650.25", Label = "CNPN 2025 (6 ans)",
            AcademicProgram = AcademicProgram.Medecine, TotalYears = 6,
        });

        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Sara", LastName = "Bennani",
            Email = "sara.bennani@etu.test", CNE = RepeaterCne, Appogee = "AP13089613",
            BacYear = "2021", AcademicProgram = AcademicProgram.Medecine,
        };
        student.AssignCnpnVersion(OldText, isInferred: false);

        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = Level3,
            StudentId = student.Id, Student = student,
        };
        registration.StampCnpnVersion(OldText, RegistrationCnpnSource.Backfilled);

        db.Users.Add(student);
        db.Registrations.Add(registration);
    });

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task AddRuleAsync() => _factory.SeedAsync(db => db.CnpnLevelEffectivities.Add(
        new CnpnLevelEffectivity
        {
            Id = RuleId, CnpnVersionId = NewText, LevelId = Level3,
            FromAcademicYearId = YearId, RecordedOn = DateTime.UtcNow,
        }));

    private Task<int?> TextOfRepeaterAsync() => _factory.QueryAsync(db =>
        db.Registrations
            .Where(r => r.Student.CNE == RepeaterCne)
            .Select(r => r.CnpnVersionId)
            .FirstOrDefaultAsync());

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    // ─── The tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// The route exists, the body binds, and the rule lands with the level and year it was sent —
    /// none of which a handler test observes, since it constructs the command itself.
    /// </summary>
    [Fact]
    public async Task A_rule_is_recorded_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync(
            $"/api/cnpn-versions/{NewText}/effectivity",
            new { levelId = Level3, fromAcademicYearId = YearId, note = "Après négociation" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var stored = await _factory.QueryAsync(db =>
            db.CnpnLevelEffectivities.AsNoTracking().SingleOrDefaultAsync());

        stored.Should().NotBeNull();
        stored!.LevelId.Should().Be(Level3);
        stored.FromAcademicYearId.Should().Be(YearId);
        stored.Note.Should().Be("Après négociation");
    }

    /// <summary>
    /// The control for every refusal below: a route that 400s on everything would satisfy them all and
    /// prove nothing. This is the request that must still get through, and it must actually move the
    /// registration.
    /// </summary>
    [Fact]
    public async Task Applying_with_the_number_that_was_shown_moves_the_registration()
    {
        await AddRuleAsync();
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var preview = await client.GetAsync($"/api/cnpn-effectivity/{RuleId}/apply/preview");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync(
            $"/api/cnpn-effectivity/{RuleId}/apply", new { confirmedMoveCount = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TextOfRepeaterAsync()).Should().Be(NewText);
    }

    /// <summary>
    /// ⚠ The case this file exists for. The confirmed count arrives in the body; a stale one must
    /// refuse — and the assertion that matters is not the status code but that <b>nothing was
    /// written</b>. A guard ordered after the write returns the same refusal and passes a handler test.
    /// </summary>
    [Fact]
    public async Task A_stale_confirmation_refuses_and_writes_nothing()
    {
        await AddRuleAsync();
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync(
            $"/api/cnpn-effectivity/{RuleId}/apply", new { confirmedMoveCount = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("CnpnEffectivity.MoveCountNotConfirmed");
        (await TextOfRepeaterAsync()).Should().Be(OldText, "the refusal must precede the write");
    }

    /// <summary>
    /// Deleting a rule is prospective: the registrations it stamped keep their text, and the route
    /// returns how many there were so the confirmation can name the number.
    /// </summary>
    [Fact]
    public async Task Deleting_a_rule_reports_what_it_governed_and_changes_none_of_it()
    {
        await AddRuleAsync();
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        await client.PostAsJsonAsync($"/api/cnpn-effectivity/{RuleId}/apply", new { confirmedMoveCount = 1 });
        (await TextOfRepeaterAsync()).Should().Be(NewText);

        var response = await client.DeleteAsync($"/api/cnpn-effectivity/{RuleId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("registrationsGoverned").GetInt32().Should().Be(1);

        (await _factory.QueryAsync(db => db.CnpnLevelEffectivities.CountAsync())).Should().Be(0);
        (await TextOfRepeaterAsync()).Should().Be(NewText, "removing a rule is not un-stamping");
    }

    /// <summary>
    /// Sending no identity leaves the request anonymous. Without this a handler that always
    /// authenticates cannot tell "allowed" from "never checked".
    /// </summary>
    [Fact]
    public async Task The_routes_are_closed_to_an_anonymous_caller()
    {
        await AddRuleAsync();
        using var client = _factory.CreateAnonymousClient();

        var read = await client.GetAsync("/api/cnpn-effectivity");
        var write = await client.PostAsJsonAsync(
            $"/api/cnpn-versions/{NewText}/effectivity",
            new { levelId = Level4, fromAcademicYearId = YearId, note = (string?)null });

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        write.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _factory.QueryAsync(db => db.CnpnLevelEffectivities.CountAsync())).Should().Be(1);
    }

    /// <summary>The filter is a real query parameter, not something the client narrows afterwards.</summary>
    [Fact]
    public async Task The_listing_filters_by_text()
    {
        await AddRuleAsync();
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var matching = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/cnpn-effectivity?cnpnVersionId={NewText}");
        var other = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/cnpn-effectivity?cnpnVersionId={OldText}");

        matching.Should().HaveCount(1);
        matching![0].GetProperty("registrationsGoverned").GetInt32()
            .Should().Be(0, "the rule has not been applied, so it governs nothing yet");
        other.Should().BeEmpty();
    }
}
