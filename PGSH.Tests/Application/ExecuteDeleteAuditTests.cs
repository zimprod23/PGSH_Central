using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.DeleteAll;
using PGSH.Application.AcademicGroups.Empty;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Cohorts.DeleteAll;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Audit;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Les trois actes destructeurs qui n'écrivent que par <c>ExecuteDelete</c> / <c>ExecuteUpdate</c>,
/// exécutés jusqu'au bout — et la ligne de journal qu'ils doivent laisser.
///
/// <para>⚠ <b>Pourquoi cette classe n'est pas dans <c>Integration/</c>, ni dans les tests de handler
/// ordinaires.</b> Le fournisseur en mémoire <b>refuse</b> <c>ExecuteDelete</c> et
/// <c>ExecuteUpdate</c> — « not supported by the current database provider ». Le chemin de succès de
/// ces trois actes n'était donc atteignable par <i>aucun</i> test du dépôt : les tests de handler ne
/// pouvaient asserter que leurs refus, et un test de bout en bout répond 500 avant d'arriver au
/// constat. C'est très exactement par ce trou que <c>DeleteAllGroupsCommand</c> et
/// <c>EmptyAllYearGroupsCommand</c> ont porté <c>IAuditableCommand</c> depuis la phase 20 en
/// n'écrivant jamais une ligne : tout passait par <c>ExecuteDelete</c>, donc hors du change tracker,
/// et rien n'appelait <c>SaveChanges</c> pour valider l'entrée mise en attente par
/// <c>AuditLogPipelineBehavior</c>. Mesuré le 10/09/2026.</para>
///
/// <para>SQLite est relationnel : il exécute les deux. ⚠ <b>Il n'est pas PostgreSQL</b> — ce que
/// l'on prouve ici est qu'un acte <i>s'exécute et laisse sa trace</i>, jamais quoi que ce soit sur
/// le schéma réel.</para>
///
/// <para>⚠ <b>Le vrai <c>AuditTrail</c>, pas le double.</b> <c>RecordingAuditTrail</c> dirait
/// seulement que le handler a <i>parlé</i> ; ce qu'il faut vérifier est que la ligne <b>arrive dans
/// le magasin</b>, ce qui est précisément ce qui manquait.</para>
/// </summary>
public class ExecuteDeleteAuditTests
{
    private static readonly DateTime Moment = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

    /// <summary>Ouvre l'entrée comme le pipeline le ferait, puis rend la piste au handler.</summary>
    private static AuditTrail OpenTrail(
        ApplicationDbContext db, string action, string entityType, string entityId, string? metadata)
    {
        var trail = new AuditTrail(db);
        trail.Open(AuditLog.Record(action, entityType, entityId, metadata, Author, Moment));
        return trail;
    }

    /// <summary>L'entrée écrite, relue depuis le magasin — jamais depuis le change tracker.</summary>
    private static async Task<JsonElement> SoleWrittenEntryAsync(ApplicationDbContext db, string action)
    {
        db.ChangeTracker.Clear();

        var entries = await db.AuditLogs.Where(a => a.Action == action).ToListAsync();
        var entry = entries.Should().ContainSingle(
            $"« {action} » a eu lieu, et le registre est la seule chose qui puisse encore le dire").Subject;

        entry.PerformedByUserId.Should().Be(Author);
        entry.Metadata.Should().NotBeNull();
        return JsonDocument.Parse(entry.Metadata!).RootElement.Clone();
    }

    /// <summary>
    /// ⚠ <b>Le test qui mord.</b> Retirer le <c>SaveChangesAsync</c> ajouté au handler et il échoue
    /// en disant exactement le défaut : les rosters sont détruits, le registre est vide.
    /// </summary>
    [Fact]
    public async Task Deleting_a_promotions_rosters_writes_its_entry()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        var stage = db.SeedCatalog();
        var groupA = db.SeedGroup(1, 1);
        var groupB = db.SeedGroup(2, 2);
        var cohort = db.SeedCohortFor(stage, groupA, 1);

        // ⚠ Rattaché à aucun roster : l'acte refuse tant qu'un roster de sa portée tient un étudiant.
        // L'affectation, elle, reste — c'est elle que « cohortsDeleted » doit compter.
        db.SeedAssignment(db.SeedRegistration("E1", "Test"), cohort);
        await db.SaveChangesAsync();

        var trail = OpenTrail(db, "PROMOTION_GROUPS_DELETED", "AcademicYear",
            TestHarness.CurrentYearId.ToString(), """{"levelId":1}""");

        var result = await new DeleteAllGroupsCommandHandler(db, new AffectationTollReader(db), trail)
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        var metadata = await SoleWrittenEntryAsync(db, "PROMOTION_GROUPS_DELETED");

        metadata.GetProperty("levelId").GetInt32().Should().Be(TestHarness.LevelId,
            "ce que la commande avait demandé survit à côté de ce que le handler a constaté");
        metadata.GetProperty("rostersDeleted").GetInt32().Should().Be(2);
        metadata.GetProperty("cohortsDeleted").GetInt32().Should().Be(1,
            "les cohortes partent avec les rosters, et c'est la moitié du coût que l'opérateur ne voit pas");

        _ = groupB;
    }

    /// <summary>Le jumeau : même cause, même correctif, autre acte.</summary>
    [Fact]
    public async Task Emptying_a_promotions_rosters_writes_its_entry()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        db.SeedCatalog();
        var group = db.SeedGroup(1, 1);
        db.SeedGroup(2, 2);
        db.SeedRegistration("E1", "Test", group);
        db.SeedRegistration("E2", "Test", group);
        await db.SaveChangesAsync();

        var trail = OpenTrail(db, "PROMOTION_GROUPS_EMPTIED", "AcademicYear",
            TestHarness.CurrentYearId.ToString(), """{"levelId":1}""");

        var result = await new EmptyAllYearGroupsCommandHandler(db, new AffectationTollReader(db), trail)
            .Handle(new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();

        var metadata = await SoleWrittenEntryAsync(db, "PROMOTION_GROUPS_EMPTIED");

        metadata.GetProperty("rostersInScope").GetInt32().Should().Be(2);
        metadata.GetProperty("registrationsDetached").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// « Réinitialiser les cohortes » — l'acte le plus destructeur de la planification, et celui qui
    /// ne déclarait rien du tout.
    /// </summary>
    [Fact]
    public async Task Resetting_a_stages_cohorts_writes_an_entry_naming_the_year_it_reached()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);

        var assignment = db.SeedAssignment(db.SeedRegistration("E1", "Test", group), cohort);
        db.SeedPeriod(assignment, service, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27),
            started: false);
        await db.SaveChangesAsync();

        // ⚠ L'année n'est pas dans la commande : « omise » veut dire « celle en cours », et seule la
        // résolution du handler sait laquelle. C'est pourquoi l'entrée ouverte ne la porte pas.
        var trail = OpenTrail(db, "STAGE_COHORTS_RESET", "Stage", TestHarness.StageId.ToString(), null);

        var result = await new DeleteAllCohortsCommandHandler(
                db, new AcademicYearResolver(db), new AffectationTollReader(db), trail)
            .Handle(new DeleteAllCohortsCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();

        var metadata = await SoleWrittenEntryAsync(db, "STAGE_COHORTS_RESET");

        metadata.GetProperty("academicYearId").GetInt32().Should().Be(TestHarness.CurrentYearId);
        metadata.GetProperty("cohortsRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("affectationsRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("periodsRemoved").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// ⚠ Un acte qui ne trouve rien à détruire s'enregistre quand même, avec son zéro : sans cela
    /// l'absence de ligne recouvrirait « personne ne l'a joué » et « quelqu'un l'a joué sur une
    /// promotion déjà vide », qui appellent des lectures opposées.
    /// </summary>
    [Fact]
    public async Task An_act_that_finds_nothing_still_writes_its_zero()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        db.SeedCatalog();
        await db.SaveChangesAsync();

        var trail = OpenTrail(db, "STAGE_COHORTS_RESET", "Stage", TestHarness.StageId.ToString(), null);

        var result = await new DeleteAllCohortsCommandHandler(
                db, new AcademicYearResolver(db), new AffectationTollReader(db), trail)
            .Handle(new DeleteAllCohortsCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();

        var metadata = await SoleWrittenEntryAsync(db, "STAGE_COHORTS_RESET");
        metadata.GetProperty("cohortsRemoved").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Un acte refusé n'écrit rien, et c'est ce qui fait du registre la liste de ce qui a eu lieu
    /// plutôt que celle des tentatives. ⚠ <b>Avec son témoin</b> : sans le second appel, un handler
    /// qui refuserait tout satisferait l'assertion sans rien prouver.
    /// </summary>
    [Fact]
    public async Task A_refused_act_writes_nothing_and_the_accepted_one_does()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        var stage = db.SeedCatalog();
        var group = db.SeedGroup(1, 1);
        db.SeedCohortFor(stage, group, 1);
        db.SeedRegistration("E1", "Test", group);
        await db.SaveChangesAsync();

        // Un roster qui tient un étudiant refuse d'être supprimé.
        var refusedTrail = OpenTrail(db, "PROMOTION_GROUPS_DELETED", "AcademicYear",
            TestHarness.CurrentYearId.ToString(), """{"levelId":1}""");

        var refused = await new DeleteAllGroupsCommandHandler(db, new AffectationTollReader(db), refusedTrail)
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        refused.IsFailure.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.AuditLogs.CountAsync()).Should().Be(0, "le registre enregistre les actes, pas les tentatives");

        // Le témoin : le même acte, une fois l'étudiant détaché.
        await db.Registrations.ExecuteUpdateAsync(
            s => s.SetProperty(r => r.AcademicGroupId, (int?)null));

        var trail = OpenTrail(db, "PROMOTION_GROUPS_DELETED", "AcademicYear",
            TestHarness.CurrentYearId.ToString(), """{"levelId":1}""");

        var accepted = await new DeleteAllGroupsCommandHandler(db, new AffectationTollReader(db), trail)
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        accepted.IsSuccess.Should().BeTrue();
        (await SoleWrittenEntryAsync(db, "PROMOTION_GROUPS_DELETED"))
            .GetProperty("rostersDeleted").GetInt32().Should().Be(1);
    }
}
