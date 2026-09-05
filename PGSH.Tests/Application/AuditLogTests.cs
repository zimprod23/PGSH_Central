using FluentAssertions;
using PGSH.Application.Audit;
using PGSH.Domain.Audit;
using PGSH.Domain.Employees;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;
using Xunit;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Application;

/// <summary>
/// Le journal des actions — la première lecture capable d'ouvrir <c>AuditLogs</c>.
///
/// <para>Trente-cinq commandes y écrivaient et rien ne pouvait le relire : la table était en
/// écriture seule. Le 02/09/2026 la question s'est posée pour de vrai — 66 rosters apparus sur la
/// 7ᵉ MED, personne ne pouvait dire d'où — et c'est cette moitié-là qui manquait le plus.</para>
/// </summary>
public class AuditLogTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Ghost = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime Day = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static GetAuditLogQueryHandler Handler(ApplicationDbContext db, params string[] roles) =>
        new(db, new PGSH.Application.Abstractions.Authorization.ExecutionAuthorizer(
            db, TestHarness.UserContext(Actor, roles)));

    private static GetAuditLogQueryHandler Scolarite(ApplicationDbContext db) =>
        Handler(db, Roles.Scolarite);

    private static AuditLog Entry(string action, DateTime at, Guid? by, string entityType = "AcademicYear",
        string? entityId = "1", string? metadata = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata,
            CreatedAt = at,
            PerformedByUserId = by,
        };

    private static void Seed(ApplicationDbContext db)
    {
        var user = new Employee { Id = Guid.NewGuid(), Email = "s@um5.ac.ma", FirstName = "Amina", LastName = "Bennani" };
        user.LinkIdentity(Actor.ToString());
        db.Users.Add(user);

        db.AuditLogs.AddRange(
            Entry("PARTITIONS_ASSIGNED", Day, Actor, metadata: "{\"levelId\":3}"),
            Entry("PARTITIONS_ASSIGNED", Day.AddHours(1), Actor),
            Entry("GROUPS_AUTO_ARRANGED", Day.AddHours(2), Actor),
            // ⚠ An actor nobody can name — the account is gone, or the base was restored without its
            // Keycloak realm. The entry must survive that; see below.
            Entry("GROUP_EMPTIED", Day.AddHours(3), Ghost, entityType: "AcademicGroup", entityId: "7"),
            Entry("PARTITIONS_CLEARED", Day.AddDays(2), null));

        db.SaveChanges();
    }

    [Fact]
    public async Task Only_the_administration_may_read_the_journal()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_administration_may_read_the_journal));
        Seed(db);

        var refused = await Handler(db, Roles.Professor).Handle(new GetAuditLogQuery(), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("Audit.NotAllowed");

        // The control: without it a handler that refuses everything satisfies the assertion above.
        (await Scolarite(db).Handle(new GetAuditLogQuery(), default)).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Newest first — a journal is read from the thing that just happened, not from the beginning.
    /// </summary>
    [Fact]
    public async Task Entries_come_back_newest_first_with_the_author_named()
    {
        await using var db = TestHarness.NewContext(nameof(Entries_come_back_newest_first_with_the_author_named));
        Seed(db);

        var page = (await Scolarite(db).Handle(new GetAuditLogQuery(), default)).Value;

        page.Entries.Items.Select(e => e.Action).Should().Equal(
            "PARTITIONS_CLEARED", "GROUP_EMPTIED", "GROUPS_AUTO_ARRANGED",
            "PARTITIONS_ASSIGNED", "PARTITIONS_ASSIGNED");

        page.Entries.Items.First(e => e.Action == "GROUPS_AUTO_ARRANGED")
            .PerformedBy.Should().Be("Amina Bennani");
    }

    /// <summary>
    /// ⚠ <b>Un auteur introuvable ne fait pas disparaître son entrée.</b> L'identifiant stocké est le
    /// <c>sub</c> Keycloak et il n'y a aucune clé étrangère derrière : le compte peut avoir été
    /// supprimé, ou la base restaurée sans son royaume Keycloak
    /// (<c>Backups:KeycloakRealmCovered</c> est <c>false</c>). Filtrer sur une jointure réussie
    /// perdrait précisément les lignes qu'on cherche quand quelque chose a mal tourné — et le brut
    /// est renvoyé à côté du nom, parce que c'est ce qui reste vrai quand l'annuaire bouge.
    /// </summary>
    [Fact]
    public async Task An_unnameable_author_keeps_his_entry_and_his_raw_id()
    {
        await using var db = TestHarness.NewContext(nameof(An_unnameable_author_keeps_his_entry_and_his_raw_id));
        Seed(db);

        var page = (await Scolarite(db).Handle(new GetAuditLogQuery(), default)).Value;

        var orphan = page.Entries.Items.Single(e => e.Action == "GROUP_EMPTIED");
        orphan.PerformedBy.Should().BeNull();
        orphan.PerformedByUserId.Should().Be(Ghost);

        // And an act performed by nobody at all — a scheduled job — is still an act.
        var scheduled = page.Entries.Items.Single(e => e.Action == "PARTITIONS_CLEARED");
        scheduled.PerformedByUserId.Should().BeNull();
        scheduled.PerformedBy.Should().BeNull();
    }

    [Fact]
    public async Task It_filters_by_action_and_by_entity()
    {
        await using var db = TestHarness.NewContext(nameof(It_filters_by_action_and_by_entity));
        Seed(db);

        var byAction = (await Scolarite(db).Handle(
            new GetAuditLogQuery(Action: "PARTITIONS_ASSIGNED"), default)).Value;
        byAction.Entries.TotalCount.Should().Be(2);

        var byEntity = (await Scolarite(db).Handle(
            new GetAuditLogQuery(EntityType: "AcademicGroup", EntityId: "7"), default)).Value;
        byEntity.Entries.Items.Should().ContainSingle()
            .Which.Action.Should().Be("GROUP_EMPTIED");
    }

    /// <summary>
    /// Les bornes sont des <b>instants</b> : <c>From</c> inclus, <c>To</c> exclu. Le client traduit
    /// « au 3 inclus » en « &lt; début du 4 <i>local</i> » ; le serveur ne fait que comparer.
    /// </summary>
    [Fact]
    public async Task The_window_includes_its_lower_bound_and_excludes_its_upper()
    {
        await using var db = TestHarness.NewContext(nameof(
            The_window_includes_its_lower_bound_and_excludes_its_upper));
        Seed(db);

        var window = (await Scolarite(db).Handle(
            new GetAuditLogQuery(From: Day, To: Day.AddHours(3)), default)).Value;

        window.Entries.TotalCount.Should().Be(3,
            "the entry exactly on the lower bound counts, the one exactly on the upper does not");
    }

    /// <summary>
    /// ⚠ <b>Le défaut que ces bornes ont corrigé, et il ne se voyait que sur des données réelles.</b>
    /// Elles étaient des <c>DateOnly</c> résolus à minuit <b>UTC</b>, alors que l'écran affiche
    /// l'heure du navigateur. Une entrée écrite le 02/09 à 22:16 UTC se lit « 03/09 00:16 » à
    /// Casablanca : filtrée « du 3 au 3 », elle disparaissait — la date lue et la date filtrée
    /// n'étaient pas la même. Trouvé en pilotant l'écran le 04/09/2026, sur le journal réel.
    ///
    /// <para>Ici la fenêtre est celle qu'un client à UTC+2 envoie pour « la journée du 3 » : du 2 à
    /// 22:00 UTC au 3 à 22:00 UTC. L'entrée de 22:16 doit s'y trouver, et une comparaison en jours
    /// UTC l'en exclurait.</para>
    /// </summary>
    [Fact]
    public async Task A_late_evening_entry_belongs_to_the_local_day_the_reader_sees()
    {
        await using var db = TestHarness.NewContext(nameof(
            A_late_evening_entry_belongs_to_the_local_day_the_reader_sees));

        // 02/09 22:16 UTC — « 03/09 00:16 » pour un lecteur à UTC+2.
        db.AuditLogs.Add(Entry("ROTATION_CYCLE_APPLIED", new DateTime(2026, 9, 2, 22, 16, 0, DateTimeKind.Utc), null));
        db.SaveChanges();

        var localThird = (await Scolarite(db).Handle(
            new GetAuditLogQuery(
                From: new DateTime(2026, 9, 2, 22, 0, 0, DateTimeKind.Utc),
                To: new DateTime(2026, 9, 3, 22, 0, 0, DateTimeKind.Utc)),
            default)).Value;

        localThird.Entries.TotalCount.Should().Be(1,
            "the reader sees it dated 03/09, so filtering on 03/09 has to return it");
    }

    /// <summary>
    /// Les puces de filtrage comptent sur tout le journal, jamais sur la fenêtre courante — sinon
    /// « PARTITIONS_ASSIGNED : 2 » deviendrait « 1 » dès que la page n'en montre qu'une, et le
    /// chemin du retour vers les autres actes disparaîtrait avec le filtre actif.
    /// </summary>
    [Fact]
    public async Task The_action_counts_describe_the_whole_journal_not_the_page()
    {
        await using var db = TestHarness.NewContext(nameof(The_action_counts_describe_the_whole_journal_not_the_page));
        Seed(db);

        var narrowed = (await Scolarite(db).Handle(
            new GetAuditLogQuery(Action: "GROUP_EMPTIED", PageSize: 1), default)).Value;

        narrowed.Entries.Items.Should().ContainSingle();
        narrowed.Actions.Should().HaveCount(4);
        narrowed.Actions.First().Should().Be(new AuditActionCount("PARTITIONS_ASSIGNED", 2));
        narrowed.TotalEntries.Should().Be(5);
    }

    /// <summary>
    /// ⚠ Une taille de page nulle veut dire « non précisée », jamais « une ligne » :
    /// <c>ToPaginatedResponseAsync</c> remonte un 0 <em>vers</em> 1.
    /// </summary>
    [Fact]
    public async Task A_zero_page_size_falls_back_to_the_default()
    {
        await using var db = TestHarness.NewContext(nameof(A_zero_page_size_falls_back_to_the_default));
        Seed(db);

        var page = (await Scolarite(db).Handle(
            new GetAuditLogQuery(PageNumber: 0, PageSize: 0), default)).Value;

        page.Entries.PageSize.Should().Be(GetAuditLogQuery.DefaultPageSize);
        page.Entries.Items.Should().HaveCount(5);
    }
}

/// <summary>
/// <see cref="AuditMetadataJson"/> — pourquoi les nouvelles entrées ne construisent plus leur JSON à
/// la main.
/// </summary>
public class AuditMetadataJsonTests
{
    /// <summary>
    /// ⚠ <b>Le cas qui a motivé l'aide.</b> Les commandes d'origine interpolent leur JSON
    /// (<c>$$"""{"levelId":{{LevelId}}}"""</c>), ce qui va tant que chaque valeur est un entier. Un
    /// libellé saisi par un admin contenant un guillemet produirait, lui, une métadonnée qui n'est
    /// pas du JSON — dans la seule colonne dont le métier est d'être relue plus tard.
    /// </summary>
    [Fact]
    public void A_label_with_a_quote_stays_valid_json()
    {
        string json = PGSH.Application.Abstractions.Messaging.AuditMetadataJson.Of(
            ("label", "Groupe \"militaire\" — 6ᵉ année"),
            ("levelId", 6))!;

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        parsed.RootElement.GetProperty("label").GetString()
            .Should().Be("Groupe \"militaire\" — 6ᵉ année");
        parsed.RootElement.GetProperty("levelId").GetInt32().Should().Be(6);
    }

    /// <summary>
    /// Rien à dire s'enregistre comme absent, pas comme <c>{}</c> — la colonne est nullable et les
    /// deux ne disent pas la même chose.
    /// </summary>
    [Fact]
    public void No_fields_is_null_rather_than_an_empty_object()
    {
        PGSH.Application.Abstractions.Messaging.AuditMetadataJson.Of().Should().BeNull();
    }

    [Fact]
    public void A_null_value_is_written_as_json_null()
    {
        string json = PGSH.Application.Abstractions.Messaging.AuditMetadataJson.Of(
            ("levelId", (int?)null))!;

        json.Should().Be("{\"levelId\":null}");
    }
}
