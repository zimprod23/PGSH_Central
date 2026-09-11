using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cohorts.UnpublishSchedule;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Dépublier toutes » as one act instead of a client-side loop.
///
/// <para>⚠ <b>Why it had to stop being a loop:</b> one HTTP request per cohorte, awaited in
/// sequence — 134 for the 3ᵉ MED — each loading its assignments with their périodes and
/// evaluations, and each invalidating the stage's cache tag so the page refetched a 134-row list
/// after every request. And <c>errorMiddleware</c> toasts every rejected mutation, so a stage with
/// several rotations underway answered with one red toast per cohorte, arriving one at a time.</para>
///
/// <para>⚠ <b>The design decision this suite pins:</b> a cohorte whose rotation has begun is
/// <b>skipped and counted</b>, never forced. Refusing the whole batch because one rotation started
/// would make the button useless from the moment it is most needed — mid-year, when the point is to
/// undo the hundred that have not begun.</para>
/// </summary>
public class UnpublishStageScheduleTests
{
    private const int ServiceId = 10;

    /// <summary>Two cohortes, each with one published période.</summary>
    private static (Cohort Quiet, Cohort Busy) SeedPublishedStage(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.Allow(stage, service);

        var slot = db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));

        var quiet = db.SeedCohortFor(stage, db.SeedGroup(1, 1), 101);
        var busy  = db.SeedCohortFor(stage, db.SeedGroup(2, 2), 102);

        int cellId = 1;
        foreach (var cohort in new[] { quiet, busy })
        {
            var cell = db.SeedSlotAssignment(cellId, cohort, slot, service);
            var registration = db.SeedRegistration($"Etu{cellId}", $"Test{cellId}", cohort.AcademicGroup);
            var assignment = db.SeedAssignment(registration, cohort);
            // ⚠ started: false — SeedPeriod defaults to true, which would make every cohorte of the
            // fixture read as underway and the sweep correctly do nothing.
            var period = db.SeedPeriod(assignment, service, slot.StartDate, slot.EndDate, started: false);
            db.SeedCoverage(period, cell);
            cellId++;
        }

        return (quiet, busy);
    }

    private static UnpublishStageScheduleCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new PGSH.Application.AcademicYears.AcademicYearResolver(db), new RecordingAuditTrail());

    [Fact]
    public async Task Every_cohorte_that_has_not_begun_is_unpublished_in_one_act()
    {
        await using var db = TestHarness.NewContext(nameof(Every_cohorte_that_has_not_begun_is_unpublished_in_one_act));
        SeedPublishedStage(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CohortsUnpublished.Should().Be(2);
        result.Value.PeriodsRemoved.Should().Be(2);
        result.Value.CohortsSkippedUnderway.Should().Be(0);

        (await db.ServicePeriods.CountAsync(p => p.CohortSlotAssignmentId != null)).Should().Be(0);
    }

    [Fact]
    public async Task A_cohorte_whose_rotation_has_begun_is_skipped_and_counted_never_forced()
    {
        // ⚠ The whole point. Forcing destroys marks and attendance, and the act allowed to do that is
        // the per-cohorte « Dépublier », which names what that one cohorte costs and asks twice.
        await using var db = TestHarness.NewContext(nameof(A_cohorte_whose_rotation_has_begun_is_skipped_and_counted_never_forced));
        var (_, busy) = SeedPublishedStage(db);
        await db.SaveChangesAsync();

        var started = await db.ServicePeriods
            .FirstAsync(p => p.InternshipAssignment.CurrentCohortId == busy.Id);
        started.IsStarted = true;
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CohortsUnpublished.Should().Be(1, "the quiet one is still undone");
        result.Value.CohortsSkippedUnderway.Should().Be(1);
        result.Value.PeriodsUnderway.Should().Be(1);
        result.Value.HeaviestSkipped.Should().ContainSingle()
              .Which.CohortId.Should().Be(busy.Id);

        (await db.ServicePeriods.CountAsync(p => p.InternshipAssignment.CurrentCohortId == busy.Id))
            .Should().Be(1, "a started rotation is left exactly as it was");
    }

    [Fact]
    public async Task Refusing_to_sweep_a_started_rotation_does_not_block_the_rest()
    {
        // The alternative design — refuse the whole batch when anything has begun — would make the
        // button useless from the moment it is most needed. Pinned so nobody "tightens" it later.
        await using var db = TestHarness.NewContext(nameof(Refusing_to_sweep_a_started_rotation_does_not_block_the_rest));
        var (quiet, _) = SeedPublishedStage(db);
        await db.SaveChangesAsync();

        foreach (var p in await db.ServicePeriods
                     .Where(p => p.InternshipAssignment.CurrentCohortId != quiet.Id).ToListAsync())
            p.IsStarted = true;
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue("a started cohorte is a skip, never a refusal for everyone");
        (await db.ServicePeriods.CountAsync(p => p.InternshipAssignment.CurrentCohortId == quiet.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_ad_hoc_periode_is_never_touched_and_is_reported()
    {
        // A période with no cell behind it is imported history, a délocalisation or a revalidation.
        // None came from a répartition and none can be recreated by publishing one.
        await using var db = TestHarness.NewContext(nameof(An_ad_hoc_periode_is_never_touched_and_is_reported));
        var (quiet, _) = SeedPublishedStage(db);
        await db.SaveChangesAsync();

        var assignment = await db.InternshipAssignments
            .FirstAsync(a => a.CurrentCohortId == quiet.Id);
        db.SeedPeriod(assignment, db.Services.Local.First(s => s.Id == ServiceId),
            new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 20), started: false);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AdHocPeriodsKept.Should().Be(1);
        (await db.ServicePeriods.CountAsync(p => p.CohortSlotAssignmentId == null)).Should().Be(1);
    }

    [Fact]
    public async Task Nothing_published_is_told_apart_from_everything_underway()
    {
        // ⚠ Zero unpublished has two causes calling for opposite acts. A bare zero collapses them —
        // the same defect as an omitted year read as « toutes les années ».
        await using var db = TestHarness.NewContext(nameof(Nothing_published_is_told_apart_from_everything_underway));
        var stage = db.SeedCatalog();
        db.Allow(stage, db.SeedService(ServiceId, "Cardiologie"));
        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));
        db.SeedCohortFor(stage, db.SeedGroup(1, 1), 101);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.NothingWasPublished.Should().BeTrue();
        result.Value.CohortsSkippedUnderway.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_stage_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_stage_is_refused));
        SeedPublishedStage(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishStageScheduleCommand(4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.NotFound");
    }

    [Fact]
    public async Task The_year_is_resolved_so_the_sweep_cannot_reach_a_past_promotion()
    {
        // ⚠ This is the one act here that deletes rows. Unscoped, "unpublish this stage" would reach
        // every year it ever ran — 563 cohortes on the worst stage in the live base.
        await using var db = TestHarness.NewContext(nameof(The_year_is_resolved_so_the_sweep_cannot_reach_a_past_promotion));
        var (quiet, _) = SeedPublishedStage(db);

        var pastYear = db.SeedAcademicYear(
            99, "2019-2020", new DateOnly(2019, 9, 1), new DateOnly(2020, 8, 31));
        var pastGroup = db.SeedGroup(9, 9, academicYearId: pastYear.Id);
        var pastCohort = db.SeedCohortFor(db.Stages.Local.First(), pastGroup, 199);
        await db.SaveChangesAsync();

        var slot = await db.StageSlots.FirstAsync();
        var service = db.Services.Local.First(s => s.Id == ServiceId);
        var cell = db.SeedSlotAssignment(99, pastCohort, slot, service);
        var pastRegistration = db.SeedRegistration("Passe", "Ancien", pastGroup, academicYearId: pastYear.Id);
        var pastAssignment = db.SeedAssignment(pastRegistration, pastCohort);
        var pastPeriod = db.SeedPeriod(pastAssignment, service, slot.StartDate, slot.EndDate, started: false);
        db.SeedCoverage(pastPeriod, cell);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new UnpublishStageScheduleCommand(TestHarness.StageId), default);

        (await db.ServicePeriods.CountAsync(p => p.InternshipAssignment.CurrentCohortId == pastCohort.Id))
            .Should().Be(1, "the past year is out of scope");
        (await db.ServicePeriods.CountAsync(p => p.InternshipAssignment.CurrentCohortId == quiet.Id))
            .Should().Be(0);
    }
}
