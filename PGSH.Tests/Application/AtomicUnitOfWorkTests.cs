using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.DeleteAll;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Audit;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Un acte destructeur atterrit entier, ou pas du tout — et sa ligne de journal avec lui.
///
/// <para>⚠ <b>Le défaut, tel qu'il se produit.</b> « Supprimer les groupes » enchaîne <b>six</b>
/// <c>ExecuteDelete</c> : périodes, historique de cohorte, affectations, cellules, cohortes, groupes.
/// Chacune est une instruction séparée, définitive dès qu'elle passe, et rien ne les liait. Une
/// requête interrompue entre la troisième et la quatrième — l'onglet fermé, la connexion coupée, et
/// ASP.NET annule alors le jeton, ce qui est le cas ordinaire et non l'exotique — laissait les
/// groupes, leurs cohortes et toute la grille en place avec <b>plus personne dedans</b> : un plan
/// complet pour zéro étudiant, que rien à l'écran ne distingue d'un plan voulu. Et le registre se
/// taisait, sa ligne étant la septième instruction.</para>
///
/// <para>⚠ <b>Pourquoi SQLite.</b> Le fournisseur en mémoire n'honore aucune transaction (le
/// warning est ignoré, donc l'enveloppe y est un <i>no-op</i>) et refuse <c>ExecuteDelete</c>. Rien
/// de ce fichier n'y serait vérifiable. SQLite est relationnel : il fait les deux. ⚠ Il n'est pas
/// PostgreSQL — ce qui est prouvé ici est qu'une unité de travail s'annule, jamais quoi que ce soit
/// sur le schéma réel.</para>
/// </summary>
public class AtomicUnitOfWorkTests
{
    private static readonly DateTime Moment = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

    private static AuditTrail OpenTrail(ApplicationDbContext db, string action, string? metadata = null)
    {
        var trail = new AuditTrail(db);
        trail.Open(AuditLog.Record(action, "AcademicYear", "1", metadata, Author, Moment));
        return trail;
    }

    /// <summary>Two rosters, one cohort, one affectation and one période hanging off it.</summary>
    private static void SeedPromotion(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var group = db.SeedGroup(1, 1);
        db.SeedGroup(2, 2);
        var cohort = db.SeedCohortFor(stage, group, 1);
        var assignment = db.SeedAssignment(db.SeedRegistration("E1", "Test"), cohort);
        db.SeedPeriod(assignment, service, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27),
            started: false);
    }

    /// <summary>
    /// ⚠ <b>Le test qui mord, et il reproduit le geste réel : l'onglet se ferme au milieu.</b> Le
    /// jeton est annulé après la première suppression, exactement comme ASP.NET l'annule quand la
    /// connexion tombe. Sans l'enveloppe, cette première suppression reste — les périodes sont
    /// détruites et tout le reste est intact.
    /// </summary>
    [Fact]
    public async Task A_unit_of_work_cancelled_half_way_leaves_nothing_behind()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        SeedPromotion(db);
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();

        var act = async () => await db.ExecuteAtomicallyAsync<int>(async ct =>
        {
            await db.ServicePeriods.ExecuteDeleteAsync(ct);

            // Le geste : la connexion tombe ici, entre deux instructions.
            await cts.CancelAsync();

            await db.CohortSlotAssignments.ExecuteDeleteAsync(ct);
            return Result.Success(0);
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        db.ChangeTracker.Clear();
        (await db.ServicePeriods.CountAsync()).Should().Be(1,
            "la transaction est annulée avec la requête : la suppression déjà passée est défaite");
    }

    /// <summary>
    /// ⚠ Un <c>Result</c> en échec rendu à mi-parcours est le même état partiel qu'une connexion
    /// coupée, et il s'annule de la même façon. Une garde placée <i>après</i> une écriture ne peut
    /// donc plus laisser de résidu — ce qui est la seule chose qui rende une garde tardive
    /// rattrapable.
    /// </summary>
    [Fact]
    public async Task A_refusal_returned_half_way_rolls_the_writes_back()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        SeedPromotion(db);
        await db.SaveChangesAsync();

        var result = await db.ExecuteAtomicallyAsync<int>(async ct =>
        {
            await db.ServicePeriods.ExecuteDeleteAsync(ct);
            return Result.Failure<int>(Error.Conflict("Test.Refused", "refusé après coup"));
        }, default);

        result.IsFailure.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.ServicePeriods.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// L'acte réel, joué jusqu'au bout : les six suppressions et la ligne de journal sont une seule
    /// transaction, et le résultat est celui d'avant. ⚠ Le témoin — sans lui, « rien ne reste » se
    /// satisferait d'un acte qui ne fait rien du tout.
    /// </summary>
    [Fact]
    public async Task The_act_still_completes_whole_when_nothing_interrupts_it()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        SeedPromotion(db);
        await db.SaveChangesAsync();

        var trail = OpenTrail(db, "PROMOTION_GROUPS_DELETED", """{"levelId":1}""");

        var result = await new DeleteAllGroupsCommandHandler(db, new AffectationTollReader(db), trail)
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        db.ChangeTracker.Clear();
        (await db.AcademicGroups.CountAsync()).Should().Be(0);
        (await db.Cohorts.CountAsync()).Should().Be(0);
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
        (await db.AuditLogs.CountAsync(a => a.Action == "PROMOTION_GROUPS_DELETED")).Should().Be(1,
            "l'acte et sa trace sont la même unité de travail");
    }

    /// <summary>
    /// ⚠ <b>Le cœur du correctif : une nouvelle tentative ne perd pas le constat, et n'écrit pas une
    /// seconde ligne.</b>
    ///
    /// <para>L'enveloppe vide le change tracker avant de rejouer. Elle relevait autrefois les entités
    /// mises en attente <i>à l'entrée</i> et les remettait telles quelles — ce qui ne peut pas marcher
    /// pour le journal, puisque <c>RecordOutcome</c> <b>remplace</b> l'entrée : la tentative suivante
    /// remettait celle d'avant, sans son constat, pendant que la piste tenait la remplaçante. Deux
    /// lignes pour un acte, dont une fausse. C'est pourquoi <c>IAuditTrail</c> disait qu'un handler
    /// devait choisir entre l'enveloppe et le constat.</para>
    ///
    /// <para>Le vide et le rejeu sont reproduits ici à la main : le mécanisme de reprise de
    /// <c>ExecuteAtomicallyAsync</c> ne se déclenche que sur une panne transitoire de la base, que
    /// rien dans ce dépôt ne peut provoquer. Ce qui est vérifié est la propriété exacte dont dépend
    /// ce mécanisme.</para>
    /// </summary>
    [Fact]
    public async Task A_retried_unit_of_work_keeps_its_outcome_and_writes_one_row()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        db.SeedCatalog();
        await db.SaveChangesAsync();

        var trail = OpenTrail(db, "PROMOTION_GROUPS_DELETED", """{"levelId":1}""");

        // Première tentative : le handler dépose son constat, puis la base tombe.
        trail.RecordOutcome(("rostersDeleted", 2));
        db.ChangeTracker.Clear();

        // Deuxième tentative : l'enveloppe rejoue l'opération depuis le début.
        var result = await trail.RunAtomicallyAsync<int>(async ct =>
        {
            trail.RecordOutcome(("rostersDeleted", 2));
            await db.SaveChangesAsync(ct);
            return Result.Success(2);
        }, default);

        result.IsSuccess.Should().BeTrue();

        db.ChangeTracker.Clear();
        var entries = await db.AuditLogs.Where(a => a.Action == "PROMOTION_GROUPS_DELETED").ToListAsync();

        var entry = entries.Should().ContainSingle("un acte, une ligne — même rejoué").Subject;
        entry.Metadata.Should().Contain("rostersDeleted",
            "c'est l'entrée que la piste tient qui est remise, pas celle d'avant le constat");
        entry.Metadata.Should().Contain("levelId", "et ce que la commande avait demandé survit avec elle");
    }

    /// <summary>
    /// ⚠ Et le collaborateur partagé n'a pas à savoir qui l'appelle : hors d'un acte auditable la
    /// piste n'a rien ouvert, donc l'enveloppe se comporte exactement comme celle du contexte. C'est
    /// ce qui permet à <c>ServiceRankWriter</c> ou à <c>CurrentYearDesignation</c> de l'utiliser sans
    /// condition.
    /// </summary>
    [Fact]
    public async Task An_unaudited_act_wraps_just_the_same()
    {
        using var connection = TestHarness.OpenSqlite();
        using var db = TestHarness.NewSqliteContext(connection);

        SeedPromotion(db);
        await db.SaveChangesAsync();

        var trail = new AuditTrail(db);   // rien d'ouvert : la commande n'est pas auditable

        var result = await trail.RunAtomicallyAsync<int>(async ct =>
        {
            await db.ServicePeriods.ExecuteDeleteAsync(ct);
            return Result.Failure<int>(Error.Conflict("Test.Refused", "refusé après coup"));
        }, default);

        result.IsFailure.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.ServicePeriods.CountAsync()).Should().Be(1, "l'annulation vaut aussi sans journal");
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }
}
