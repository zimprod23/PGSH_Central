using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Cohorts.PublishSchedule;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Audit;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Ce que la publication laisse au registre.
///
/// <para>⚠ <b>Pourquoi cette classe existe.</b> « Dépublier » était audité depuis la phase 20 et
/// « Publier » ne l'était pas : le registre tenait le <i>défaire</i> sans le <i>faire</i>, sur l'acte
/// qui crée les <c>ServicePeriod</c> — c'est-à-dire tout ce que les chefs notent et tout ce que les
/// présences visent. La seule trace d'une publication était l'absence de sa dépublication.</para>
///
/// <para>⚠ <b>Et passer outre le refus d'un service est une décision.</b> <c>AllowOverCapacity</c>
/// n'est pas un réglage : c'est le geste que quelqu'un pose contre un service ayant déclaré ne pas
/// vouloir être dépassé, et c'est exactement ce qu'on viendra demander au registre trois mois plus
/// tard.</para>
///
/// <para>⚠ <b>Le vrai <c>AuditTrail</c>, jamais un double</b> — ce qu'il faut prouver est que la
/// ligne <b>arrive dans le magasin</b>, pas que le handler a parlé. Même raison que
/// <c>ExecuteDeleteAuditTests</c>.</para>
/// </summary>
public class PublishScheduleAuditTests
{
    private const int ServiceId = 1;
    private const int CohortId  = 10;

    private static readonly DateOnly P1Start = new(2026, 3, 2);
    private static readonly DateOnly P1End   = new(2026, 3, 27);
    private static readonly DateTime Moment  = new(2026, 9, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    /// <summary>
    /// Ouvre l'entrée comme <c>AuditLogPipelineBehavior</c> le ferait — à partir de la commande
    /// elle-même, pour que la métadonnée vérifiée ici soit celle qui partira en production.
    /// </summary>
    private static AuditTrail OpenTrail(ApplicationDbContext db, IAuditableCommand command)
    {
        var trail = new AuditTrail(db);
        trail.Open(AuditLog.Record(
            command.AuditAction, command.AuditEntityType, command.AuditEntityId,
            command.AuditMetadata, Author, Moment));
        return trail;
    }

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

    /// <summary>Une cohorte de <paramref name="students"/> routée par une cellule vers un service.</summary>
    private static async Task<Cohort> SeedGridAsync(ApplicationDbContext db, int students, int capacity = 20)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = capacity;

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);

        for (int i = 0; i < students; i++)
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", cohort.AcademicGroup), cohort);

        await db.SaveChangesAsync();
        return cohort;
    }

    /// <summary>
    /// ⚠ <b>Le constat porte un nombre.</b> « Publier » sur une cohorte de quatre et sur une de
    /// quarante écrivaient la même ligne — et « combien cela a-t-il emporté » est la question qu'on
    /// pose au registre, pas « qui a cliqué ».
    /// </summary>
    [Fact]
    public async Task Publishing_a_cohort_records_how_many_periods_it_created()
    {
        await using var db = TestHarness.NewContext("publish-audit-cohort");
        await SeedGridAsync(db, students: 4);

        var command = new PublishCohortScheduleCommand(CohortId);
        var result = await new PublishCohortScheduleCommandHandler(db, Publisher(db), OpenTrail(db, command))
            .Handle(command, default);

        result.IsSuccess.Should().BeTrue();

        var metadata = await SoleWrittenEntryAsync(db, "COHORT_SCHEDULE_PUBLISHED");
        metadata.GetProperty("periodsCreated").GetInt32().Should().Be(4);
        metadata.GetProperty("allowOverCapacity").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Le passage outre voyage avec l'entrée. Sans lui, une publication forcée et une publication
    /// ordinaire sont indiscernables — et c'est la première que l'on cherche.
    /// </summary>
    [Fact]
    public async Task Forcing_a_publication_past_a_services_ceiling_is_recorded_as_such()
    {
        await using var db = TestHarness.NewContext("publish-audit-forced");
        await SeedGridAsync(db, students: 4, capacity: 1);

        var command = new PublishCohortScheduleCommand(CohortId, AllowOverCapacity: true);
        var result = await new PublishCohortScheduleCommandHandler(db, Publisher(db), OpenTrail(db, command))
            .Handle(command, default);

        result.IsSuccess.Should().BeTrue("le dépassement était demandé explicitement");

        (await SoleWrittenEntryAsync(db, "COHORT_SCHEDULE_PUBLISHED"))
            .GetProperty("allowOverCapacity").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// L'acte à l'échelle du stage, avec l'année qu'il a <i>résolue</i> : la commande ne la porte pas,
    /// puisqu'une année omise veut dire « celle en cours » et que seule la résolution sait laquelle.
    /// </summary>
    [Fact]
    public async Task Publishing_a_stage_records_the_year_it_resolved_and_what_it_reached()
    {
        await using var db = TestHarness.NewContext("publish-audit-stage");
        await SeedGridAsync(db, students: 3);

        var command = new PublishStageScheduleCommand(TestHarness.StageId);
        var result = await new PublishStageScheduleCommandHandler(
                db, new AcademicYearResolver(db), Publisher(db), OpenTrail(db, command))
            .Handle(command, default);

        result.IsSuccess.Should().BeTrue();

        var metadata = await SoleWrittenEntryAsync(db, "STAGE_SCHEDULE_PUBLISHED");
        metadata.GetProperty("academicYearId").GetInt32().Should().Be(TestHarness.CurrentYearId);
        metadata.GetProperty("cohortsPublished").GetInt32().Should().Be(1);
        metadata.GetProperty("periodsCreated").GetInt32().Should().Be(3);
        metadata.GetProperty("cohortsSkipped").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// ⚠ <b>Le test qui mord.</b> Le publisher n'enregistre que s'il a des périodes à poser, donc un
    /// <c>SaveChanges</c> conditionnel dans le handler ferait disparaître l'entrée exactement ici :
    /// « Publier » rejoué sur un stage déjà publié — l'acte le plus banal d'une campagne — ne
    /// laisserait rien. Et zéro cohorte publiée n'est pas un non-acte : c'est le constat que tout
    /// l'était déjà, ce qui est une réponse et non un silence.
    /// </summary>
    [Fact]
    public async Task Publishing_a_stage_that_was_already_published_still_writes_its_entry()
    {
        await using var db = TestHarness.NewContext("publish-audit-noop");
        await SeedGridAsync(db, students: 3);

        (await Publisher(db).PublishCohortAsync(CohortId, false, default)).IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();

        var command = new PublishStageScheduleCommand(TestHarness.StageId);
        var result = await new PublishStageScheduleCommandHandler(
                db, new AcademicYearResolver(db), Publisher(db), OpenTrail(db, command))
            .Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsCreated.Should().Be(0);

        var metadata = await SoleWrittenEntryAsync(db, "STAGE_SCHEDULE_PUBLISHED");
        metadata.GetProperty("cohortsPublished").GetInt32().Should().Be(0);
        metadata.GetProperty("cohortsSkipped").GetInt32().Should().Be(1);
        metadata.GetProperty("periodsCreated").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Un acte refusé n'écrit rien — ⚠ <b>avec son témoin</b>, sans lequel un handler qui refuserait
    /// tout satisferait l'assertion sans rien prouver.
    /// </summary>
    [Fact]
    public async Task A_refused_publication_writes_nothing_and_the_accepted_one_does()
    {
        await using var db = TestHarness.NewContext("publish-audit-refusal");
        await SeedGridAsync(db, students: 2, capacity: 1);

        var refused = new PublishCohortScheduleCommand(CohortId);
        var outcome = await new PublishCohortScheduleCommandHandler(db, Publisher(db), OpenTrail(db, refused))
            .Handle(refused, default);

        outcome.IsFailure.Should().BeTrue("le service est plein et rien n'a autorisé le dépassement");

        db.ChangeTracker.Clear();
        (await db.AuditLogs.CountAsync()).Should().Be(0, "le registre enregistre les actes, pas les tentatives");

        var forced = new PublishCohortScheduleCommand(CohortId, AllowOverCapacity: true);
        var accepted = await new PublishCohortScheduleCommandHandler(db, Publisher(db), OpenTrail(db, forced))
            .Handle(forced, default);

        accepted.IsSuccess.Should().BeTrue();
        (await SoleWrittenEntryAsync(db, "COHORT_SCHEDULE_PUBLISHED"))
            .GetProperty("periodsCreated").GetInt32().Should().Be(2);
    }
}
