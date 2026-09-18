using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Audit;
using PGSH.Domain.Stages;
using Xunit;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// Un acte auditable qui <b>réussit sans rien changer</b> écrit-il quand même sa ligne&nbsp;?
///
/// <para>⚠ <b>C'est une question à laquelle aucun test de handler ne peut répondre, et le défaut est
/// invisible par construction.</b> <c>AuditLogPipelineBehavior</c> met la ligne en attente
/// <em>avant</em> le handler, et seul le <c>SaveChanges</c> de celui-ci la valide. C'est ce qui fait
/// qu'un acte <b>refusé</b> n'écrit rien — la propriété que l'on veut — mais c'est aussi ce qui fait
/// qu'un acte dont le <c>SaveChanges</c> est <b>conditionnel</b> n'écrit rien lorsque sa condition est
/// fausse. Les deux se ressemblent parfaitement : rien dans le code ne relie l'entrée à la sauvegarde,
/// rien ne se compile en erreur, et un acte réussi qui n'écrit rien est indiscernable d'un acte refusé
/// qui n'écrit rien.</para>
///
/// <para>Or « personne n'a joué cet acte » et « quelqu'un l'a joué sans effet » appellent des lectures
/// opposées, et c'est très exactement ce que le registre existe pour départager. Le cas n'est pas
/// exotique : <b>c'est le rejeu du bouton</b> — redécouper une promotion déjà découpée, resemer un
/// calendrier déjà semé, recloner un texte déjà cloné.</para>
///
/// <para>⚠ <b>Six sorties étaient dans ce cas au 14/09/2026</b>, trouvées en finissant le balayage de
/// l'item 0bd — celui du 10/09 n'avait retenu que les handlers n'appelant <em>jamais</em>
/// <c>SaveChanges</c>. Chacune a son cas ici, et chacune vérifie aussi que le constat déposé par
/// <c>IAuditTrail</c> porte ses zéros : une ligne qui ne dirait pas « zéro » laisserait le lecteur
/// supposer un effet.</para>
/// </summary>
public class NoEffectAuditEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int ServiceId = 1;
    private const int LabelledGroupId = 1;
    private const int UnlabelledGroupId = 2;

    private readonly ApiFactory _factory;

    public NoEffectAuditEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Un roster <b>déjà</b> partitionné et un service autorisé portant déjà son mode : le point de
    /// départ de chaque acte sans effet mesuré ici.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");

        db.SeedGroup(LabelledGroupId, 1, rotationGroup: "A");

        db.StageAllowedServices.Add(new StageAllowedService
        {
            StageId = stage.Id,
            ServiceId = service.Id,
            Rank = 1,
            PlacementMode = ServicePlacementMode.Rotation,
        });
    });

    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    /// <summary>L'entrée unique de cet acte, avec ses métadonnées lues comme du JSON.</summary>
    private async Task<JsonElement> SoleEntryAsync(string action)
    {
        var entries = await _factory.QueryAsync(db =>
            db.AuditLogs.Where(a => a.Action == action).ToListAsync());

        var entry = entries.Should().ContainSingle(
            $"« {action} » a réussi, donc le registre en tient une ligne — même s'il n'a rien changé")
            .Subject;

        entry.Metadata.Should().NotBeNull($"« {action} » doit dire ce qu'il a emporté, fût-ce zéro");
        return JsonDocument.Parse(entry.Metadata!).RootElement.Clone();
    }

    /// <summary>
    /// ⚠ Le cas ordinaire, et celui qui a vécu le plus longtemps : rejouer « découper » sur une
    /// promotion dont chaque roster porte déjà sa partition. <c>AssignUnlabelled</c> ne rend alors
    /// aucune affectation, et le <c>SaveChanges</c> était sous <c>if (assignments.Count &gt; 0)</c>.
    /// </summary>
    [Fact]
    public async Task Re_cutting_an_already_partitioned_promotion_still_records_the_act()
    {
        var response = await Client().PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={TestHarness.CurrentYearId}"
            + $"&levelId={TestHarness.LevelId}",
            new { partitionCount = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("PARTITIONS_ASSIGNED");

        metadata.GetProperty("labeled").GetInt32().Should().Be(0,
            "aucun roster n'était à étiqueter — et c'est le fait que l'entrée doit porter");
        metadata.GetProperty("reassigned").GetInt32().Should().Be(0);
        metadata.GetProperty("totalGroups").GetInt32().Should().Be(1,
            "« zéro étiqueté sur un roster » et « zéro étiqueté sur zéro roster » sont deux états");
    }

    /// <summary>
    /// Découper une promotion qui n'a aucun roster. ⚠ Distinct du cas ci-dessus : ici l'acte sort
    /// avant même de calculer une répartition, et le registre doit pouvoir dire lequel des deux a eu
    /// lieu — d'où <c>totalGroups</c>.
    /// </summary>
    [Fact]
    public async Task Cutting_a_promotion_that_has_no_rosters_still_records_the_act()
    {
        var response = await Client().PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={TestHarness.PreviousYearId}"
            + $"&levelId={TestHarness.LevelId}",
            new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("PARTITIONS_ASSIGNED");

        metadata.GetProperty("totalGroups").GetInt32().Should().Be(0);
        metadata.GetProperty("labeled").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Dé-partitionner une promotion qui ne porte aucune partition. L'inverse de l'acte ci-dessus, et
    /// il souffrait du même défaut sur ses <b>deux</b> sorties sans effet.
    /// </summary>
    [Fact]
    public async Task Clearing_partitions_that_are_not_there_still_records_the_act()
    {
        await _factory.SeedAsync(db => db.SeedGroup(UnlabelledGroupId, 2, rotationGroup: null,
            academicYearId: TestHarness.PreviousYearId));

        var response = await Client().DeleteAsync(
            $"/api/groups/partitions?academicYearId={TestHarness.PreviousYearId}"
            + $"&levelId={TestHarness.LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("PARTITIONS_CLEARED");

        metadata.GetProperty("cleared").GetInt32().Should().Be(0);
        metadata.GetProperty("totalGroups").GetInt32().Should().Be(1,
            "sans ce nombre, « rien à retirer » et « aucun roster » se lisent pareil");
    }

    /// <summary>
    /// Resemer les fériés d'une année déjà semée : aucun jour n'est ajouté. ⚠ Le rejeu est ici la
    /// manière normale de s'en servir — on reseme après avoir corrigé une date à la main.
    /// </summary>
    [Fact]
    public async Task Re_seeding_holidays_that_are_all_present_still_records_the_act()
    {
        var first = await Client().PostAsync(
            $"/api/calendar/holidays/seed-national?academicYearId={TestHarness.CurrentYearId}", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await Client().PostAsync(
            $"/api/calendar/holidays/seed-national?academicYearId={TestHarness.CurrentYearId}", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _factory.QueryAsync(db =>
            db.AuditLogs.Where(a => a.Action == "HOLIDAYS_NATIONAL_SEEDED").ToListAsync());

        entries.Should().HaveCount(2,
            "l'acte a été joué deux fois ; le second n'a rien ajouté, ce qui ne le rend pas inexistant");

        var replay = JsonDocument.Parse(entries.Last().Metadata!).RootElement;

        replay.GetProperty("created").GetInt32().Should().Be(0);
        replay.GetProperty("alreadyPresent").GetInt32().Should().BeGreaterThan(0,
            "c'est ce nombre qui explique le zéro d'à côté");
    }

    /// <summary>
    /// Reposer sur un service autorisé le mode de placement qu'il porte déjà. ⚠ Le plus discret des
    /// six : l'acte sort sur une simple égalité, et « réserver un service déjà réservé » est ce que
    /// fait un double-clic.
    /// </summary>
    [Fact]
    public async Task Setting_a_placement_mode_to_the_one_already_held_still_records_the_act()
    {
        var response = await Client().PutAsJsonAsync(
            $"/api/stages/{TestHarness.StageId}/allowed-services/{ServiceId}/placement-mode",
            new { placementMode = nameof(ServicePlacementMode.Rotation) });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metadata = await SoleEntryAsync("STAGE_SERVICE_PLACEMENT_MODE_SET");

        metadata.GetProperty("changed").GetBoolean().Should().BeFalse(
            "retirer un service de la rotation et confirmer qu'il y est déjà sont deux événements");
    }

    /// <summary>
    /// ⚠ <b>Le témoin.</b> Sans lui, chaque cas ci-dessus passerait aussi bien si l'acte écrivait
    /// <em>toujours</em> une ligne, refus compris — or c'est la propriété inverse qui fait du registre
    /// la liste de ce qui a eu lieu plutôt que la liste des tentatives.
    /// </summary>
    [Fact]
    public async Task A_refused_act_still_writes_nothing()
    {
        var response = await Client().PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={TestHarness.CurrentYearId}"
            + $"&levelId={TestHarness.LevelId}",
            new { partitionCount = 0 });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        var entries = await _factory.QueryAsync(db =>
            db.AuditLogs.Where(a => a.Action == "PARTITIONS_ASSIGNED").ToListAsync());

        entries.Should().BeEmpty("un acte refusé n'a pas eu lieu");
    }
}
