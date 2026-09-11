using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Audit;
using Xunit;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// Les actes qui écrivent et défont la grille de planification laissent-ils une trace, et cette
/// trace dit-elle <b>combien</b>&nbsp;?
///
/// <para>⚠ <b>Aucun test de handler ne peut répondre.</b> La ligne n'est pas écrite par le handler :
/// <c>AuditLogPipelineBehavior</c> l'ajoute au contexte et c'est le <c>SaveChanges</c> de l'acte qui
/// la valide. Un handler peut donc déclarer <c>IAuditableCommand</c>, passer tous ses tests, et
/// n'écrire jamais rien.</para>
///
/// <para>Le second sujet est le <i>constat</i>. Un code d'acte seul ne distingue pas « vider cette
/// colonne » joué sur une colonne vide de la même chose jouée sur une colonne entièrement publiée :
/// deux événements sans rapport, la même ligne. Ce que l'acte a emporté vient donc du handler par
/// <c>IAuditTrail</c>, et cette classe est ce qui le vérifie de bout en bout.</para>
///
/// <para>⚠ <b>Les trois actes qui n'écrivent que par <c>ExecuteDelete</c></b> — « Réinitialiser les
/// cohortes », « Supprimer les groupes », « Vider les groupes » — <b>ne peuvent pas être joués
/// ici</b> : le fournisseur en mémoire refuse <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>, donc la
/// route répond 500 avant d'atteindre quoi que ce soit. Ils sont couverts par
/// <c>ExecuteDeleteAuditTests</c>, sur SQLite — et ce trou-là est exactement celui par lequel leur
/// défaut a survécu.</para>
/// </summary>
public class DestructivePlanningAuditEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int ServiceId = 1;
    private const int OtherServiceId = 2;
    private const int PlainSlotId = 100;
    private const int PublishedSlotId = 101;
    private const int PlainCohortId = 1;
    private const int PublishedCohortId = 2;

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End = new(2026, 3, 27);
    private static readonly DateOnly LateStart = new(2026, 4, 6);
    private static readonly DateOnly LateEnd = new(2026, 4, 30);

    private readonly ApiFactory _factory;

    public DestructivePlanningAuditEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Deux colonnes et deux cohortes, séparées à dessein : l'une porte une cellule <b>publiée</b>,
    /// l'autre rien.
    /// </summary>
    /// <remarks>
    /// <para>⚠ La séparation est ce qui rend la classe lisible. La publication verrouille <i>la
    /// cohorte</i> autant que la cellule — épingler refuse dès qu'une période publiée pend à la
    /// cohorte — donc une cohorte unique ferait refuser la moitié des actes mesurés ici, et l'on
    /// mesurerait des refus en croyant mesurer des constats.</para>
    ///
    /// <para>⚠ La période n'est <b>pas</b> démarrée. <c>SeedPeriod</c> démarre par défaut, ce qui
    /// rendrait <c>AffectationToll.IsUnderway</c> vrai et ferait refuser la dépublication.</para>
    /// </remarks>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedService(OtherServiceId, "Pédiatrie");

        var plainSlot = db.SeedSlot(stage, PlainSlotId, 1, Start, End);
        var publishedSlot = db.SeedSlot(stage, PublishedSlotId, 2, LateStart, LateEnd);

        var groupA = db.SeedGroup(1, 1);
        var groupB = db.SeedGroup(2, 2);

        var plain = db.SeedCohortFor(stage, groupA, PlainCohortId);
        var published = db.SeedCohortFor(stage, groupB, PublishedCohortId);

        db.SeedSlotAssignment(1, plain, plainSlot, service);

        var cell = db.SeedSlotAssignment(2, published, publishedSlot, service);
        var assignment = db.SeedAssignment(db.SeedRegistration("E1", "Test", groupB), published);
        var period = db.SeedPeriod(assignment, service, LateStart, LateEnd, started: false);
        db.SeedCoverage(period, cell);
    });

    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    private Task<List<AuditLog>> EntriesAsync(string action) => _factory.QueryAsync(db =>
        db.AuditLogs.Where(a => a.Action == action).ToListAsync());

    /// <summary>L'entrée unique de cet acte, avec ses métadonnées lues comme du JSON.</summary>
    private async Task<JsonElement> SoleEntryAsync(string action)
    {
        var entries = await EntriesAsync(action);
        var entry = entries.Should().ContainSingle(
            $"« {action} » a eu lieu une fois, donc le registre en tient une ligne").Subject;

        entry.PerformedByUserId.Should().Be(ApiFactory.AdminIdentityId,
            "une ligne sans auteur ne répond pas à la question qu'on lui pose");

        entry.Metadata.Should().NotBeNull($"« {action} » doit dire ce qu'il a emporté");
        return JsonDocument.Parse(entry.Metadata!).RootElement.Clone();
    }

    /// <summary>
    /// ⚠ Dépublier détruit des notes de chef et des journées de présence, qui cascadent avec la
    /// période et ne survivent nulle part ailleurs. Le registre est alors la seule chose qui puisse
    /// encore dire qu'elles ont existé.
    /// </summary>
    [Fact]
    public async Task Unpublishing_a_cohorts_schedule_records_what_cascaded_with_it()
    {
        var response = await Client().DeleteAsync($"/api/cohorts/{PublishedCohortId}/publish-schedule");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("COHORT_SCHEDULE_UNPUBLISHED");

        metadata.GetProperty("forced").GetBoolean().Should().BeFalse(
            "une dépublication ordinaire et un passage en force ne doivent pas se lire pareil");
        metadata.GetProperty("periodsRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("evaluationsLost").GetInt32().Should().Be(0);
        metadata.GetProperty("attendanceDaysLost").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Vider une colonne rapporte <b>les deux</b> nombres. À zéro, « supprimées » recouvre une
    /// colonne déjà vide et une colonne entièrement publiée, qui appellent des actes opposés.
    /// </summary>
    [Fact]
    public async Task Clearing_a_column_says_what_it_kept_as_well_as_what_it_took()
    {
        var response = await Client().DeleteAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PublishedSlotId}/cohorts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("STAGE_SLOT_CELLS_CLEARED");

        metadata.GetProperty("cellsCleared").GetInt32().Should().Be(0);
        metadata.GetProperty("cellsKeptPublished").GetInt32().Should().Be(1,
            "sans ce nombre, l'acte se lit comme une colonne qui était vide");
    }

    /// <summary>
    /// Épingler nomme celui qui a pris la décision — c'est la moitié « qui » de ce que
    /// <c>PinnedCellsKept</c> compte, et une cellule épinglée est ce que la répartition automatique
    /// n'a plus le droit de réécrire.
    /// </summary>
    [Fact]
    public async Task Pinning_a_cell_records_the_service_it_replaced()
    {
        var response = await Client().PutAsJsonAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PlainSlotId}/cohorts/{PlainCohortId}",
            new { serviceId = OtherServiceId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = await SoleEntryAsync("COHORT_SLOT_PINNED");

        metadata.GetProperty("serviceId").GetInt32().Should().Be(OtherServiceId);
        metadata.GetProperty("replacedServiceId").GetInt32().Should().Be(ServiceId);
        metadata.GetProperty("wasPinned").GetBoolean().Should().BeFalse(
            "écraser une cellule que la rotation avait placée et écraser le choix d'un collègue "
            + "sont deux actes, et le code seul ne les sépare pas");
    }

    /// <summary>
    /// ⚠ Déplacer un créneau décale une promotion entière, et les dates d'avant ne survivent nulle
    /// part : c'est le registre ou rien.
    /// </summary>
    [Fact]
    public async Task Moving_a_period_records_the_dates_it_overwrote()
    {
        var response = await Client().PutAsJsonAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PlainSlotId}",
            new
            {
                academicYearId = TestHarness.CurrentYearId,
                periodNumber = 1,
                label = (string?)null,
                startDate = "2026-03-09",
                endDate = "2026-03-31",
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metadata = await SoleEntryAsync("STAGE_SLOT_UPDATED");

        metadata.GetProperty("fromStartDate").GetString().Should().Be("2026-03-02");
        metadata.GetProperty("fromEndDate").GetString().Should().Be("2026-03-27");
        metadata.GetProperty("toStartDate").GetString().Should().Be("2026-03-09");
    }

    /// <summary>
    /// Un acte refusé n'écrit rien — la propriété de forme du pipeline, reprise ici sur un acte
    /// destructeur, qui est là où elle compte.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Avec son témoin.</b> Une route qui refuserait tout satisferait l'assertion du haut sans
    /// rien prouver ; c'est le second appel, qui doit réussir, qui lui donne son sens.
    /// </remarks>
    [Fact]
    public async Task A_refused_destructive_act_writes_no_entry()
    {
        // Un créneau couvert par une période publiée ne se supprime pas.
        var refused = await Client().DeleteAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PublishedSlotId}");

        refused.IsSuccessStatusCode.Should().BeFalse();
        (await EntriesAsync("STAGE_SLOT_DELETED")).Should().BeEmpty(
            "le registre enregistre ce qui a eu lieu, pas les tentatives");

        // Le témoin : la même route, sur la colonne que rien ne publie.
        var accepted = await Client().DeleteAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PlainSlotId}");

        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metadata = await SoleEntryAsync("STAGE_SLOT_DELETED");
        metadata.GetProperty("periodNumber").GetInt32().Should().Be(1);
        metadata.GetProperty("startDate").GetString().Should().Be("2026-03-02");
    }

    /// <summary>
    /// ⚠ Un acte qui ne trouve rien à faire s'enregistre quand même. Sans cela l'absence de ligne
    /// recouvrirait « personne ne l'a joué » et « quelqu'un l'a joué sans effet » — la règle « dire
    /// ce que le blanc veut dire », appliquée au registre.
    /// </summary>
    [Fact]
    public async Task An_act_without_effect_is_still_recorded()
    {
        var response = await Client().DeleteAsync(
            $"/api/stages/{TestHarness.StageId}/slots/{PlainSlotId}/cohorts/{PublishedCohortId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metadata = await SoleEntryAsync("COHORT_SLOT_CLEARED");
        metadata.GetProperty("cellExisted").GetBoolean().Should().BeFalse(
            "cette cohorte n'a pas de cellule sur cette colonne, et l'acte a bien eu lieu");
    }
}
