using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>DELETE levels/{levelId}/rotation-cycle</c> through the real pipeline.
///
/// <para>⚠ What no handler test can see here is <b>how the block reaches the handler</b>. The stages
/// arrive as a repeated query parameter (<c>?stageIds=1&amp;stageIds=2</c>) bound to an
/// <c>int[]</c>; a handler test hands the list over directly. If that binding broke, the command would
/// arrive with an empty array — and an empty array is not a harmless no-op here, it is « supprimer le
/// bloc » resolving to no stages at all. The frontend's own serializer emits the repeated form for
/// exactly this reason, and RTK's default (<c>stageIds=1,2</c>) does not bind.</para>
/// </summary>
public class RotationCycleEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int PromotionId = 5;
    private const int MedecineId = 40;
    private const int ChirurgieId = 41;
    private const int PediatrieId = 42;

    private readonly ApiFactory _factory;

    public RotationCycleEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Two stages carrying an axis, and a third that carries one of its own — the second block, which
    /// a removal scoped to the level rather than to the stages would take with it.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId, Label = "2025-2026", IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        });

        var level = new Level
        {
            Id = PromotionId, Label = "Cinquième Année Médecine", Year = 5,
            AcademicProgram = AcademicProgram.Medecine,
        };
        db.Levels.Add(level);

        foreach (var (id, name) in new[]
                 {
                     (MedecineId, "Médecine"), (ChirurgieId, "Chirurgie"), (PediatrieId, "Pédiatrie"),
                 })
        {
            db.Stages.Add(new Stage { Id = id, Name = name, LevelId = PromotionId, Level = level, Coefficient = 1 });
        }

        int slotId = 500;
        foreach (int stageId in new[] { MedecineId, ChirurgieId })
        {
            foreach (int period in new[] { 1, 2 })
            {
                db.StageSlots.Add(new StageSlot
                {
                    Id = slotId++, StageId = stageId, AcademicYearId = YearId,
                    PeriodNumber = period, Label = $"P{period}",
                    StartDate = new DateOnly(2025, 10, 1).AddMonths(period - 1),
                    EndDate = new DateOnly(2025, 10, 31).AddMonths(period - 1),
                });
            }
        }

        // The other block: same promotion, its own semester.
        db.StageSlots.Add(new StageSlot
        {
            Id = slotId, StageId = PediatrieId, AcademicYearId = YearId,
            PeriodNumber = 1, Label = "P1",
            StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28),
        });
    });

    private static string Url(params int[] stageIds) =>
        $"/api/levels/{PromotionId}/rotation-cycle?academicYearId={YearId}"
        + string.Concat(stageIds.Select(id => $"&stageIds={id}"));

    private Task<int> SlotCountAsync(int stageId) =>
        _factory.QueryAsync(db => db.StageSlots.CountAsync(s => s.StageId == stageId));

    [Fact]
    public async Task Deleting_a_block_removes_the_slots_of_the_stages_named_in_the_query()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync(Url(MedecineId, ChirurgieId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("slotsRemoved").GetInt32().Should().Be(4,
            "both stage ids have to survive the query-string binding — one would remove two");

        (await SlotCountAsync(MedecineId)).Should().Be(0);
        (await SlotCountAsync(ChirurgieId)).Should().Be(0);

        // ⚠ The control that makes the assertion above mean something: the other block of the same
        // promotion is untouched.
        (await SlotCountAsync(PediatrieId)).Should().Be(1);
    }

    /// <summary>
    /// The route with no stages at all. It must be refused by validation rather than reaching the
    /// handler as « remove nothing » — the shape a mis-serialized array arrives in.
    /// </summary>
    [Fact]
    public async Task Deleting_with_no_stages_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync($"/api/levels/{PromotionId}/rotation-cycle?academicYearId={YearId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SlotCountAsync(MedecineId)).Should().Be(2, "nothing was removed");
    }

    [Fact]
    public async Task Deleting_a_block_that_is_not_there_says_so_rather_than_reporting_success()
    {
        using var client = _factory.CreateApiClient();

        // Pédiatrie's own block, removed twice: the second call has nothing to remove.
        await client.DeleteAsync(Url(PediatrieId));
        var response = await client.DeleteAsync(Url(PediatrieId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Anonymous callers get nowhere — the route is not a hole in the authenticated surface.</summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_remove_a_block()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.DeleteAsync(Url(MedecineId, ChirurgieId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SlotCountAsync(MedecineId)).Should().Be(2);
    }
}
