using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Audit;
using PGSH.Domain.Registrations;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// Le journal des actions, de bout en bout : l'acte est-il <b>réellement</b> enregistré, et
/// peut-on le relire&nbsp;?
///
/// <para>⚠ <b>Aucun test de handler ne peut répondre à ça.</b> La trace n'est pas écrite par le
/// handler : <c>AuditLogPipelineBehavior</c> ajoute la ligne au contexte et c'est le
/// <c>SaveChanges</c> du handler qui la valide. Il faut donc la vraie chaîne pour savoir si un acte
/// laisse une trace — et surtout si un acte <i>refusé</i> n'en laisse pas.</para>
///
/// <para>C'est la question du 02/09/2026, posée sur des données réelles : 66 rosters étaient apparus
/// sur la 7ᵉ MED et le journal tenait deux lignes pour toute la session.</para>
/// </summary>
public class AuditLogEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int PromotionId = 3;
    private const int RetraitId = 16;
    private const string Journal = "/api/audit-log";

    private readonly ApiFactory _factory;

    public AuditLogEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Une promotion à découper, et le marqueur de retrait — dont le découpage est refusé. Les deux
    /// sont nécessaires : c'est la paire « acte accepté / acte refusé » qui dit ce que le journal
    /// enregistre.
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

        db.Levels.Add(new Level
        {
            Id = RetraitId, Label = "Retrait", Year = 0, AcademicProgram = AcademicProgram.Medecine,
        });

        foreach (int n in Enumerable.Range(1, 4))
            db.AcademicGroups.Add(new AcademicGroup
            {
                Id = n, Label = $"Groupe {n}", GroupNumber = n,
                AcademicYearId = YearId, LevelId = PromotionId,
            });
    });

    private HttpClient Scolarite() => _factory.CreateApiClient(null, Roles.Scolarite);

    private static string CutUrl(int levelId) =>
        $"/api/groups/assign-partitions?academicYearId={YearId}&levelId={levelId}";

    private Task<int> AuditRowsAsync(string action) => _factory.QueryAsync(db =>
        db.AuditLogs.CountAsync(a => a.Action == action));

    private static async Task<AuditLogPage> ReadJournalAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuditLogPage>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    /// <summary>
    /// ⚠ <b>Le cœur de la fonctionnalité.</b> Découper une promotion crée des rosters — l'acte qui a
    /// provoqué la question — et n'écrivait rien, alors que son défaire
    /// (<c>PARTITIONS_CLEARED</c>) était enregistré depuis toujours.
    /// </summary>
    [Fact]
    public async Task Cutting_a_promotion_is_recorded_with_its_author_and_its_criteria()
    {
        using var client = Scolarite();

        (await AuditRowsAsync("PARTITIONS_ASSIGNED")).Should().Be(0);

        var cut = await client.PostAsJsonAsync(CutUrl(PromotionId), new { partitionCount = 2 });
        cut.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await ReadJournalAsync(
            await client.GetAsync($"{Journal}?action=PARTITIONS_ASSIGNED"));

        var entry = page.Entries.Items.Should().ContainSingle().Subject;

        entry.EntityType.Should().Be("AcademicYear");
        entry.EntityId.Should().Be(YearId.ToString());
        entry.PerformedByUserId.Should().Be(ApiFactory.AdminIdentityId);
        entry.PerformedBy.Should().Be("Integration Caller",
            "the journal has to name a person, not a GUID");

        // The criteria are what make the entry answer « pourquoi cette promotion a-t-elle 2
        // partitions ? » rather than merely « quelqu'un a découpé quelque chose ».
        using var metadata = JsonDocument.Parse(entry.Metadata!);
        metadata.RootElement.GetProperty("levelId").GetInt32().Should().Be(PromotionId);
        metadata.RootElement.GetProperty("partitionCount").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// ⚠ <b>Un acte refusé ne laisse pas de trace, et c'est voulu.</b> Le journal enregistre ce qui
    /// <i>a eu lieu</i> ; noter les tentatives ferait du registre une liste de choses qui ne se sont
    /// pas produites, dans laquelle celles qui se sont produites deviennent difficiles à trouver.
    ///
    /// <para>Ce n'est pas un choix explicite du pipeline mais une conséquence de sa forme — la ligne
    /// est ajoutée au contexte avant le handler et n'est validée que par le <c>SaveChanges</c> de
    /// celui-ci — donc c'est exactement le genre de propriété qui se perd sans test.</para>
    /// </summary>
    [Fact]
    public async Task A_refused_act_writes_no_entry()
    {
        using var client = Scolarite();

        var refused = await client.PostAsJsonAsync(CutUrl(RetraitId), new { partitionCount = 2 });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await AuditRowsAsync("PARTITIONS_ASSIGNED")).Should().Be(0,
            "the trail records acts, not attempts");
    }

    /// <summary>
    /// Le journal couvre les actes de toute la faculté : c'est une lecture d'administration. Sans
    /// jeton du tout, la requête est anonyme — une lecture qui authentifie toujours ne saurait pas
    /// distinguer « autorisé » de « jamais vérifié ».
    /// </summary>
    [Fact]
    public async Task The_journal_is_administrative_and_authenticated()
    {
        using var stranger = _factory.CreateApiClient(null, Roles.Professor);
        (await stranger.GetAsync(Journal)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = _factory.CreateAnonymousClient();
        (await anonymous.GetAsync(Journal)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The control, without which a route that refuses everything passes both assertions above.
        using var client = Scolarite();
        (await client.GetAsync(Journal)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Les filtres arrivent par la query string, donc leur liaison n'est vérifiable que par la vraie
    /// chaîne — un <c>action</c> perdu en route élargirait silencieusement la lecture à tout le
    /// journal, ce qui ressemble à un filtre qui ne sert à rien plutôt qu'à une erreur.
    /// </summary>
    [Fact]
    public async Task The_filters_survive_the_query_string()
    {
        using var client = Scolarite();

        await client.PostAsJsonAsync(CutUrl(PromotionId), new { partitionCount = 2 });
        await client.DeleteAsync($"/api/groups/partitions?academicYearId={YearId}&levelId={PromotionId}");

        var everything = await ReadJournalAsync(await client.GetAsync(Journal));
        everything.Entries.TotalCount.Should().BeGreaterThan(1);

        var narrowed = await ReadJournalAsync(
            await client.GetAsync($"{Journal}?action=PARTITIONS_ASSIGNED"));

        narrowed.Entries.Items.Should().OnlyContain(e => e.Action == "PARTITIONS_ASSIGNED");
        narrowed.Entries.TotalCount.Should().BeLessThan(everything.Entries.TotalCount);

        // ⚠ The chips are counted over the whole journal, never over the filtered window: narrowed to
        // one action they must still name the others, or there is no way back to them.
        narrowed.Actions.Should().HaveCountGreaterThan(1);
        narrowed.TotalEntries.Should().Be(everything.Entries.TotalCount);
    }
}
