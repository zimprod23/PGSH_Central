using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Registrations;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>POST groups/assign-partitions</c> through the real pipeline.
///
/// <para>These cover what <c>WithdrawalMarkerLevelTests</c> cannot see. That suite calls the handler
/// directly, so it proves the rule; it cannot prove the route reaches the rule, that
/// <c>levelId</c> is genuinely required, that a refusal comes back as a 400 carrying its code, or —
/// the one that matters most here — that the guard runs <b>before</b> anything is written. A guard
/// ordered after the write returns the same <c>Result.Failure</c> and passes the handler test.</para>
/// </summary>
public class PartitionEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int PromotionId = 3;
    private const int RetraitId = 16;

    private readonly ApiFactory _factory;

    public PartitionEndpointTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Every test starts from an empty store. The host is shared across the class, so without this a
    /// test that writes labels leaves them for the next one to trip over.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One promotion with rosters to cut, and the withdrawal marker with rosters of its own — the
    /// marker really does carry them (10 in the live base), which is exactly why refusing it is not
    /// the same as it having nothing to act on.
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
            Id = PromotionId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        // Year 0: the Access base's CODE_N = 'MED00', a withdrawal marker kept as a Level so the
        // registrations and the stages already served that year survived the import.
        db.Levels.Add(new Level
        {
            Id = RetraitId, Label = "Retrait", Year = 0, AcademicProgram = AcademicProgram.Medecine,
        });

        foreach (var n in Enumerable.Range(1, 4))
            db.AcademicGroups.Add(new AcademicGroup
            {
                Id = n, Label = $"Groupe {n}", GroupNumber = n,
                AcademicYearId = YearId, LevelId = PromotionId,
            });

        foreach (var n in Enumerable.Range(1, 2))
            db.AcademicGroups.Add(new AcademicGroup
            {
                Id = 100 + n, Label = $"Groupe {50 + n}", GroupNumber = 50 + n,
                AcademicYearId = YearId, LevelId = RetraitId,
            });
    });

    private static string Url(int levelId, int yearId = YearId) =>
        $"/api/groups/assign-partitions?academicYearId={yearId}&levelId={levelId}";

    private async Task<int> LabelledCountAsync(int levelId) => await _factory.QueryAsync(db =>
        db.AcademicGroups.CountAsync(g => g.LevelId == levelId && g.RotationGroup != null));

    private static async Task<(string? Title, string? Detail)> ProblemAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null,
            doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null);
    }

    /// <summary>
    /// The refusal the manual smoke step could never reach: once the marker stopped being offered in
    /// the pickers, forcing the act by hand needed a bearer token, and the step went unexecuted twice.
    /// </summary>
    [Fact]
    public async Task The_withdrawal_marker_is_refused_by_the_endpoint()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url(RetraitId), new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (title, detail) = await ProblemAsync(response);
        title.Should().Be("Levels.NotAPromotion");
        detail.Should().Contain("Retrait", "the refusal has to name the level it is about");
    }

    /// <summary>
    /// ⚠ The point of the whole file. A guard placed after the write returns the same failure and is
    /// indistinguishable from a correct one at the handler boundary — only the store tells them apart.
    /// </summary>
    [Fact]
    public async Task And_writes_nothing_while_refusing()
    {
        using var client = _factory.CreateApiClient();

        (await LabelledCountAsync(RetraitId)).Should().Be(0);

        await client.PostAsJsonAsync(Url(RetraitId), new { partitionCount = 2 });

        (await LabelledCountAsync(RetraitId)).Should().Be(0,
            "a refused cut must leave the marker's rosters exactly as it found them");
    }

    /// <summary>
    /// The control. Without it, a route that 400s on everything — a binding failure, a typo in the
    /// path — would satisfy the refusal tests above and prove nothing about the guard.
    /// </summary>
    [Fact]
    public async Task A_real_promotion_is_still_cut()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url(PromotionId), new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LabelledCountAsync(PromotionId)).Should().Be(4);
        (await LabelledCountAsync(RetraitId)).Should().Be(0, "cutting one promotion must not reach another level");
    }

    /// <summary>
    /// <c>levelId</c> became a required query parameter because year-wide the cut reached every
    /// promotion of the year plus « Non réparti ». The compiler enforces it on the command; only the
    /// pipeline enforces it on the request.
    /// </summary>
    [Fact]
    public async Task Omitting_the_level_is_refused_rather_than_applied_year_wide()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={YearId}", new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LabelledCountAsync(RetraitId)).Should().Be(0);
    }

    /// <summary>
    /// Not a formality: the whole authorization layer is invisible to handler tests, so "the endpoint
    /// requires a caller" has never been asserted anywhere until now.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_cut_a_promotion()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Url(PromotionId), new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
