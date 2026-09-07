using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.AllowedServices;
using PGSH.Application.Stages.Delocalization.Bulk;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using PGSH.Application.Students.Selection;

namespace PGSH.Tests.Application;

// A service hors faculté — the KENITRA row the catalogue holds only so a délocalisation has
// something to name. It is not a rotation candidate, and its number is not a ceiling anybody should
// read. Two halves are asserted here: that nothing can plan into it, and that a student who left
// stops occupying the service he left.
public class ExternalServiceTests
{
    private const int ExternalServiceId = 70;
    private const int HomeServiceId     = 1;

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End   = new(2026, 3, 29);

    [Fact]
    public async Task An_external_service_cannot_be_authorised_on_a_stage()
    {
        await using var db = TestHarness.NewContext("external-not-allowed");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);
        await db.SaveChangesAsync();

        var result = await new AddAllowedServiceCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new AddAllowedServiceCommand(stage.Id, ExternalServiceId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ExternalServiceNotAllowedInStage);
        (await db.StageAllowedServices.CountAsync()).Should().Be(0);
    }

    // ⚠ 25 of the 27 stages authorise no service at all, so the whitelist guards nothing on them
    // and this is the only thing standing between a hand-placed cell and a fictional ceiling in the
    // saturation of a real service's neighbours.
    [Fact]
    public async Task A_cell_cannot_be_placed_on_an_external_service_by_hand_either()
    {
        await using var db = TestHarness.NewContext("external-no-cell");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        db.SeedSlot(stage, 1, 1, Start, End);
        await db.SaveChangesAsync();

        var result = await new SetCohortSlotAssignmentCommandHandler(db, new GroupScheduleConflictGuard(db))
            .Handle(new SetCohortSlotAssignmentCommand(cohort.Id, 1, ExternalServiceId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ExternalServiceNotAllowedInStage);
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_arranger_drops_an_external_service_authorised_before_the_flag_existed()
    {
        await using var db = TestHarness.NewContext("external-arranger");
        var stage = db.SeedCatalog();
        var home = db.SeedService(HomeServiceId, "Cardiologie");
        var external = db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);

        // Straight onto the navigation: the command refuses this, and a row created before it did
        // must still not become a column of the rotation.
        stage.AllowedServices.Add(home);
        stage.AllowedServices.Add(external);

        db.SeedCohort(stage, 10, "Groupe 10");
        db.SeedSlot(stage, 1, 1, Start, End);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            stage.Id, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        var cells = await db.CohortSlotAssignments.ToListAsync();
        cells.Should().NotBeEmpty();
        cells.Should().OnlyContain(c => c.ServiceId == HomeServiceId);
    }

    // ⚠ The number the whole operation exists to move. Délocalising leaves the student in his
    // cohorte — that is what makes the act reversible — so a load counted per cohorte membership
    // would relieve the grid by exactly nothing.
    [Fact]
    public async Task A_delocalized_student_stops_occupying_the_service_he_left()
    {
        await using var db = TestHarness.NewContext("external-occupancy");
        var stage = db.SeedCatalog();
        var home = db.SeedService(HomeServiceId, "Cardiologie");
        db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var slot = db.SeedSlot(stage, 1, 1, Start, End);
        db.SeedSlotAssignment(1, cohort, slot, home);

        var students = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var registration = db.SeedRegistration($"Etudiant{i}", $"Nom{i}", cohort.AcademicGroup);
                var assignment = db.SeedAssignment(registration, cohort);
                db.SeedPeriod(assignment, home, Start, End, started: false);
                return registration;
            })
            .ToList();

        await db.SaveChangesAsync();

        var before = await new ServiceOccupancyCalculator(db).BuildAsync([HomeServiceId], default);
        before.LoadOn(HomeServiceId, Start, End).Should().Be(3);

        var applied = await db.BulkDelocalizeHandler().Handle(
            new ApplyBulkDelocalizationCommand(
                stage.Id, ExternalServiceId, "Saturation",
                new StudentTargets(RegistrationIds: [students[0].Id, students[1].Id]),
                ConfirmedCount: 2, StartDate: Start, EndDate: End),
            default);

        applied.IsSuccess.Should().BeTrue();

        var after = await new ServiceOccupancyCalculator(db).BuildAsync([HomeServiceId], default);
        after.LoadOn(HomeServiceId, Start, End).Should().Be(1, "two of the three are in Kénitra");
    }
}
