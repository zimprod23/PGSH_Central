using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Hospitals.Services.Delete;
using PGSH.Domain.Audit;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Ce qui retient un service, et ce que sa suppression emporte.
///
/// <para>⚠ <b>Le défaut.</b> Le handler ne gardait rien : il retirait et sauvegardait, avec un
/// commentaire « (e.g., Check if students are currently assigned to this service) » à la place de la
/// garde. Un service porté par la grille ou par des périodes remontait donc en
/// <c>DbUpdateException</c> — 23503, nom de contrainte PostgreSQL — soit un <b>500</b> sans une
/// phrase que l'opérateur puisse lire. Et un service libre partait avec ses quotas, l'historique de
/// ses chefs et son personnel, sans que rien ne dise combien.</para>
/// </summary>
public class DeleteServiceGuardTests
{
    private static readonly DateTime Moment = new(2026, 9, 11, 16, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.Parse("1b2c3d4e-5f6a-4b7c-8d9e-0f1a2b3c4d5e");

    private static AuditTrail OpenTrail(ApplicationDbContext db, IAuditableCommand command)
    {
        var trail = new AuditTrail(db);
        trail.Open(AuditLog.Record(
            command.AuditAction, command.AuditEntityType, command.AuditEntityId,
            command.AuditMetadata, Author, Moment));
        return trail;
    }

    private static async Task<Result> DeleteAsync(ApplicationDbContext db, int serviceId)
    {
        var command = new DeleteServiceCommand(serviceId);
        return await new DeleteServiceCommandHandler(db, OpenTrail(db, command)).Handle(command, default);
    }

    /// <summary>
    /// Le service porte une cellule de la grille. ⚠ En mémoire aucune clé étrangère n'est tenue : sans
    /// garde explicite la suppression <b>réussit ici</b> et n'échoue qu'en production. C'est le trou
    /// que décrit <c>CLAUDE.md</c>, et la raison pour laquelle ce test vaut quelque chose.
    /// </summary>
    [Fact]
    public async Task A_service_placed_in_the_grid_is_refused()
    {
        await using var db = TestHarness.NewContext("delete-service-cell");
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);
        var slot = db.SeedSlot(stage, 100, 1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27));
        db.SeedSlotAssignment(1, cohort, slot, service);
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, service.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.StillInUse");
        result.Error.Description.Should().Contain("cellule(s) de planning");

        db.ChangeTracker.Clear();
        (await db.Services.CountAsync()).Should().Be(1, "un refus ne détruit rien");
        (await db.AuditLogs.CountAsync()).Should().Be(0,
            "le registre enregistre les actes, pas les tentatives");
    }

    /// <summary>
    /// Le cas ordinaire sur cette base : le service a reçu des étudiants. ⚠ Le conseil n'est alors
    /// <b>pas</b> « retirez ces rattachements d'abord » — une période est au dossier de l'étudiant et
    /// ne se retire pas — mais « retirez-le des listes de services autorisés ».
    /// </summary>
    [Fact]
    public async Task A_service_that_has_taken_students_is_refused_and_told_it_cannot_be_cleared()
    {
        await using var db = TestHarness.NewContext("delete-service-history");
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, 1, "G1");
        var registration = db.SeedRegistration("Sara", "Bennani");
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, service, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27));
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, service.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("période(s) de stage déjà enregistrée(s)");
        result.Error.Description.Should().Contain("dossier des étudiants");
        result.Error.Description.Should().NotContain("Retirez ces rattachements d'abord");
    }

    /// <summary>
    /// ⚠ Le stage qui autorise le service <b>refuse</b> au lieu de cascader : la ligne porte un rang et
    /// un mode de placement, et <c>ServiceRotationOrder</c> tient les rangs pour contigus depuis 1 —
    /// une ligne retirée par la base laisserait un trou. Le refus <b>nomme le stage</b>, comme
    /// <c>DeleteStageCommand</c> nomme le texte CNPN.
    /// </summary>
    [Fact]
    public async Task A_service_a_stage_authorises_is_refused_and_the_stage_is_named()
    {
        await using var db = TestHarness.NewContext("delete-service-whitelist");
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        db.StageAllowedServices.Add(new StageAllowedService
        {
            StageId = stage.Id, ServiceId = service.Id, Rank = 1,
        });
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, service.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain(stage.Name);
        result.Error.Description.Should().Contain("Retirez ces rattachements d'abord");
    }

    /// <summary>
    /// ⚠ <b>Les raisons sont comptées ensemble.</b> Retirer le service d'un stage pour s'entendre dire
    /// ensuite qu'il porte des cellules, c'est faire le tour deux fois — et le second tour ressemble à
    /// un premier correctif qui aurait échoué.
    /// </summary>
    [Fact]
    public async Task Every_reason_is_named_at_once_not_one_per_attempt()
    {
        await using var db = TestHarness.NewContext("delete-service-both");
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);
        var slot = db.SeedSlot(stage, 100, 1, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27));
        db.SeedSlotAssignment(1, cohort, slot, service);
        db.StageAllowedServices.Add(new StageAllowedService
        {
            StageId = stage.Id, ServiceId = service.Id, Rank = 1,
        });
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, service.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should()
            .Contain("cellule(s) de planning").And.Contain(stage.Name);
    }

    /// <summary>
    /// Le témoin, et le constat. Rien ne retient le service : il part, et le registre dit ce que la
    /// cascade a emporté — les seuls nombres qui ne se relisent nulle part ensuite.
    /// </summary>
    [Fact]
    public async Task A_free_service_is_deleted_and_the_register_says_what_the_cascade_took()
    {
        await using var db = TestHarness.NewContext("delete-service-ok");
        var chef = db.SeedChef(Guid.NewGuid());
        var service = db.SeedService(1, "Cardiologie", chef);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 12);
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, service.Id);

        result.IsSuccess.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.Services.CountAsync()).Should().Be(0);

        var entry = (await db.AuditLogs.Where(a => a.Action == "SERVICE_DELETED").ToListAsync())
            .Should().ContainSingle().Subject;

        var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
        metadata.GetProperty("serviceName").GetString().Should().Be("Cardiologie");
        metadata.GetProperty("quotasRemoved").GetInt32().Should().Be(1,
            "les quotas partent en cascade et rien d'autre ne pourra plus dire combien");
        metadata.GetProperty("chefTenuresRemoved").GetInt32().Should().Be(1);
        metadata.GetProperty("staffDetached").GetInt32().Should().Be(1);
    }

    /// <summary>Un service qui n'existe pas est un 404, pas un 500 ni un succès muet.</summary>
    [Fact]
    public async Task An_unknown_service_is_not_found()
    {
        await using var db = TestHarness.NewContext("delete-service-missing");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await DeleteAsync(db, 4242);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.NotFound");
    }
}
