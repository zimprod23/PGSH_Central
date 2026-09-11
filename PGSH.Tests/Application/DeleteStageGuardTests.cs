using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Delete;
using PGSH.Domain.Audit;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Ce qui retient un stage, et ce que sa suppression emporte.
///
/// <para>⚠ <b>Le défaut, rencontré à l'usage le 11/09/2026.</b> Supprimer un stage rattaché à un CNPN
/// remontait en <c>DbUpdateException</c> — <c>23503 … viole la contrainte
/// « FK_CurriculumStages_Stages_StageId »</c> — donc un <b>500</b> dont le seul contenu était le nom
/// d'une contrainte PostgreSQL. Le handler ne gardait rien : il retirait et sauvegardait. L'opérateur
/// a dû deviner qu'il fallait d'abord retirer le stage du texte.</para>
///
/// <para>⚠ <b>Et l'autre moitié ne s'annonçait pas du tout</b> : <c>StageSlots</c>,
/// <c>StageAllowedServices</c> et <c>StageObjectives</c> sont en <b>CASCADE</b>. Un stage dont l'axe
/// est posé perd ses créneaux — de toutes les années — avec l'ordre des services et les modes
/// « Réservé ». La cascade est le bon marché et elle reste ; ce qui manquait est que <i>personne
/// n'était prévenu de son ampleur</i>.</para>
/// </summary>
public class DeleteStageGuardTests
{
    private static readonly DateTime Moment = new(2026, 9, 11, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

    private static AuditTrail OpenTrail(ApplicationDbContext db, IAuditableCommand command)
    {
        var trail = new AuditTrail(db);
        trail.Open(AuditLog.Record(
            command.AuditAction, command.AuditEntityType, command.AuditEntityId,
            command.AuditMetadata, Author, Moment));
        return trail;
    }

    /// <summary>
    /// Le cas signalé : le stage est exigé par un texte. ⚠ Le refus <b>nomme le texte</b> — « 1 CNPN »
    /// laisserait l'opérateur chercher lequel, et lui dire où aller est toute la raison d'être du refus.
    /// </summary>
    [Fact]
    public async Task A_stage_required_by_a_cnpn_is_refused_and_the_text_is_named()
    {
        await using var db = TestHarness.NewContext("delete-stage-cnpn");
        var stage = db.SeedCatalog();
        db.SeedCurriculumStage(TestHarness.NewCnpnId, stage);
        await db.SaveChangesAsync();

        var command = new DeleteStageCommand(stage.Id);
        var result = await new DeleteStageCommandHandler(db, OpenTrail(db, command))
            .Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.StillInUse");
        result.Error.Description.Should().Contain("CNPN");

        db.ChangeTracker.Clear();
        (await db.Stages.CountAsync()).Should().Be(1, "un refus ne détruit rien");
        (await db.AuditLogs.CountAsync()).Should().Be(0, "le registre enregistre les actes, pas les tentatives");
    }

    /// <summary>
    /// ⚠ <b>Les raisons sont comptées ensemble.</b> Retirer le stage de son texte pour s'entendre dire
    /// ensuite qu'il porte des cohortes, c'est faire le tour deux fois — et le second tour ressemble à
    /// un premier correctif qui aurait échoué.
    /// </summary>
    [Fact]
    public async Task Every_reason_is_named_at_once_not_one_per_attempt()
    {
        await using var db = TestHarness.NewContext("delete-stage-both");
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(1, 1);
        db.SeedCohortFor(stage, group, 1);
        db.SeedCurriculumStage(TestHarness.NewCnpnId, stage);
        await db.SaveChangesAsync();

        var command = new DeleteStageCommand(stage.Id);
        var result = await new DeleteStageCommandHandler(db, OpenTrail(db, command))
            .Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("cohorte(s)").And.Contain("CNPN");
    }

    /// <summary>
    /// ⚠ <b>Le test qui mord.</b> Sans la garde, ce cas atteint <c>SaveChanges</c> : en mémoire il
    /// passerait (aucune FK n'y est tenue), et sur PostgreSQL il remonterait en 500. C'est exactement
    /// le trou que décrit <c>CLAUDE.md</c> — le fournisseur *in-memory* ne voit aucune contrainte, donc
    /// seul un refus **explicite** est vérifiable ici.
    /// </summary>
    [Fact]
    public async Task The_guard_is_what_refuses_not_the_database()
    {
        await using var db = TestHarness.NewContext("delete-stage-guard-bites");
        var stage = db.SeedCatalog();
        db.SeedCurriculumStage(TestHarness.NewCnpnId, stage);
        await db.SaveChangesAsync();

        var command = new DeleteStageCommand(stage.Id);
        var result = await new DeleteStageCommandHandler(db, OpenTrail(db, command))
            .Handle(command, default);

        result.IsFailure.Should().BeTrue(
            "le magasin en mémoire ne tient aucune FK : s'il n'y a pas de garde, la suppression réussit ici "
            + "et n'échoue qu'en production");
    }

    /// <summary>
    /// Le témoin, et le constat. Rien ne retient le stage : il part, et le registre dit ce que la
    /// cascade a emporté — les seuls nombres qui ne se relisent nulle part ensuite.
    /// </summary>
    [Fact]
    public async Task A_free_stage_is_deleted_and_the_register_says_what_the_cascade_took()
    {
        await using var db = TestHarness.NewContext("delete-stage-ok");
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        db.SeedSlot(stage, 100, 1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27));
        db.SeedSlot(stage, 101, 2, new DateOnly(2026, 4, 6), new DateOnly(2026, 5, 1));
        db.SeedObjective(stage, 1, "Savoir ausculter", 1);
        db.StageAllowedServices.Add(new PGSH.Domain.Stages.StageAllowedService
        {
            StageId = stage.Id, ServiceId = service.Id,
        });
        await db.SaveChangesAsync();

        var command = new DeleteStageCommand(stage.Id);
        var result = await new DeleteStageCommandHandler(db, OpenTrail(db, command))
            .Handle(command, default);

        result.IsSuccess.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.Stages.CountAsync()).Should().Be(0);

        var entry = (await db.AuditLogs.Where(a => a.Action == "STAGE_DELETED").ToListAsync())
            .Should().ContainSingle().Subject;

        var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
        metadata.GetProperty("slotsRemoved").GetInt32().Should().Be(2,
            "les créneaux partent en cascade et rien d'autre ne pourra plus dire combien");
        metadata.GetProperty("allowedServicesRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("objectivesRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("stageName").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
