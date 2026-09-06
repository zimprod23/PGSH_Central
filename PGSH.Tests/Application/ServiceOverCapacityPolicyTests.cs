using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Hospitals.Services;
using PGSH.Application.Hospitals.Services.Create;
using PGSH.Application.Hospitals.Services.GetById;
using PGSH.Application.Hospitals.Services.Update;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Schedule;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Certains chefs de service n'acceptent pas qu'on dépasse leur effectif. »
/// <c>Service.AllowsOverCapacity</c> is that statement, and this file holds the three things it has
/// to be at once.
///
/// <list type="bullet">
///   <item><b>Allowed by default.</b> The column lands on 148 services nobody has asked, and a
///   service that has never refused anything must publish exactly as it did before the flag
///   existed.</item>
///   <item><b>Binding when it is set</b>, including against the checkbox — which is the entire
///   point. « Autoriser le dépassement » is ticked as a matter of routine on a base where 233 of
///   353 planned cells are over capacity, so a ceiling nothing could make firm was in practice
///   advisory for everybody.</item>
///   <item><b>About the number and nothing else.</b> A firm service still takes its promotions and
///   still publishes inside its capacity; what it refuses is being pushed past it.</item>
/// </list>
///
/// ⚠ The trap this suite exists to hold shut: the override used to mean the occupancy half could be
/// <i>skipped entirely</i> — the loads were never counted when the box was ticked. Under that
/// shortcut a firm service is unreachable, because its number is never read. Several tests below
/// publish with <c>allowOverCapacity: true</c> for exactly that reason.
/// </summary>
public class ServiceOverCapacityPolicyTests
{
    private const int ServiceId     = 1;
    private const int SecondSvcId   = 2;
    private const int CohortId      = 10;
    private const int SecondCohortId = 20;
    private const int OtherLevelId  = 77;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    /// <summary>One cohorte of <paramref name="students"/> routed through P1 into one service.</summary>
    private static async Task<Service> SeedGridAsync(
        ApplicationDbContext db, int students, int capacity = 20, bool allowsOverCapacity = true)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = capacity;
        service.AllowsOverCapacity = allowsOverCapacity;

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);

        for (int i = 0; i < students; i++)
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", cohort.AcademicGroup), cohort);

        await db.SaveChangesAsync();
        return service;
    }

    // ── The default ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The one property the whole migration rests on. A service nobody has restricted has refused
    /// nothing, and the column arrives on 148 of them at once: false by default would have turned the
    /// next publication of every promotion into a wall, over a restriction no chef ever stated.
    /// </summary>
    [Fact]
    public void A_service_allows_the_override_until_somebody_says_otherwise()
    {
        new Service().AllowsOverCapacity.Should().BeTrue();
    }

    [Fact]
    public async Task A_service_that_has_said_nothing_still_takes_the_overflow()
    {
        await using var db = TestHarness.NewContext("firm-default-publishes");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsSuccess.Should().BeTrue("nothing about this service has changed");
        (await db.ServicePeriods.CountAsync()).Should().Be(25);
    }

    // ── What the flag refuses ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_firm_service_is_not_forced_past_its_capacity()
    {
        await using var db = TestHarness.NewContext("firm-total-refused");
        await SeedGridAsync(db, students: 25, capacity: 20, allowsOverCapacity: false);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.OverCapacityRefusedByService");
        result.Error.Description.Should()
            .Contain("25").And.Contain("20")
            .And.Contain("n'autorise pas le dépassement",
                "the checkbox is on screen promising otherwise, so the refusal has to say it does not reach this service");
        (await db.ServicePeriods.CountAsync()).Should().Be(0, "the refusal runs before the write");
    }

    /// <summary>
    /// The quota half, which is a different remedy and therefore a different sentence: raising the
    /// promotion's quota, not the service's total. Same verdict — the checkbox does not reach it.
    /// </summary>
    [Fact]
    public async Task A_firm_services_quota_is_not_forced_either()
    {
        await using var db = TestHarness.NewContext("firm-quota-refused");
        var service = await SeedGridAsync(db, students: 12, capacity: 20, allowsOverCapacity: false);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.OverCapacityRefusedByService");
        result.Error.Description.Should()
            .Contain("quota", "the ceiling in force is the promotion's, and that is what has to be raised")
            .And.Contain("12").And.Contain("10");
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// ⚠ The control, and the one that would fail under the old shortcut in the other direction: a
    /// firm service is not a closed one. It refuses the <i>overrun</i>, and a plan inside its number
    /// publishes without anybody being asked anything.
    /// </summary>
    [Fact]
    public async Task A_firm_service_inside_its_capacity_publishes_like_any_other()
    {
        await using var db = TestHarness.NewContext("firm-within-capacity");
        await SeedGridAsync(db, students: 15, capacity: 20, allowsOverCapacity: false);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsSuccess.Should().BeTrue("15 ≤ 20 — there is nothing here to refuse");
        (await db.ServicePeriods.CountAsync()).Should().Be(15);
    }

    /// <summary>
    /// Without the override the sentence is the ordinary one: the admin has not asked to force
    /// anything yet, so what he needs to read is the number, and the checkbox is still worth
    /// offering... on the services that allow it.
    /// </summary>
    [Fact]
    public async Task Without_the_override_a_permissive_service_still_gets_the_ordinary_refusal()
    {
        await using var db = TestHarness.NewContext("permissive-plain-refusal");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.CapacityExceeded");
    }

    // ── The aggregate refusal ─────────────────────────────────────────────────────

    /// <summary>
    /// A stage-wide publish meets several cells at once, and the two unforceable halves are fixed in
    /// different places: one is a promotion the service does not take, the other a service that does
    /// not accept being over its number. Naming them separately is what tells the reader which of the
    /// two acts is owed — and, when nothing is left over, the sentence stops offering a checkbox that
    /// would change nothing.
    /// </summary>
    [Fact]
    public async Task The_aggregate_refusal_counts_the_two_unforceable_halves_apart()
    {
        await using var db = TestHarness.NewContext("firm-aggregate");
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);

        var firm = db.SeedService(ServiceId, "Cardiologie");
        firm.Capacity = 5;
        firm.AllowsOverCapacity = false;

        var pharmacieOnly = db.SeedService(SecondSvcId, "Toxicologie");
        db.SeedLevelCapacity(pharmacieOnly, OtherLevelId, 15);

        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);

        var overFilled = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, overFilled, slot, firm);
        for (int i = 0; i < 9; i++)
            db.SeedAssignment(db.SeedRegistration($"A{i}", "Test", overFilled.AcademicGroup), overFilled);

        var refused = db.SeedCohort(stage, SecondCohortId, "Groupe 20");
        db.SeedSlotAssignment(2, refused, slot, pharmacieOnly);
        for (int i = 0; i < 3; i++)
            db.SeedAssignment(db.SeedRegistration($"B{i}", "Test", refused.AcademicGroup), refused);

        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, allowOverCapacity: true, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.PublishRefusedByIntake");
        result.Error.Description.Should()
            .Contain("n'accueille pas cette promotion")
            .And.Contain("n'autorise pas le dépassement")
            .And.NotContain("cochez",
                "every refused cell here is unforceable, and offering the checkbox against them is what "
                + "teaches an admin that the screen is lying");
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
    }

    // ── What the grid says before anybody presses publish ─────────────────────────

    /// <summary>
    /// The saturation list is read <i>before</i> the publish, and the numbers of a firm service and
    /// of a permissive one are identical — only the service says which is which. So the fact travels
    /// on the row rather than being re-derived on the client, for the reason
    /// <c>ServicePeriodResponse.State</c> does: one rule, two sides of a network boundary.
    /// </summary>
    [Fact]
    public async Task The_grid_marks_a_firm_services_saturation_as_unforceable()
    {
        await using var db = TestHarness.NewContext("firm-grid-saturation");
        await SeedGridAsync(db, students: 25, capacity: 20, allowsOverCapacity: false);

        var grid = await new GetStageScheduleQueryHandler(
                db,
                new AcademicYearResolver(db),
                new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new GetStageScheduleQuery(TestHarness.StageId), default);

        grid.IsSuccess.Should().BeTrue();
        var saturation = grid.Value.Summary.Saturations.Should().ContainSingle().Subject;
        saturation.Reason.Should().Be(SaturationReason.Total, "the ceiling in force is the service's own");
        saturation.Forceable.Should().BeFalse();
    }

    [Fact]
    public async Task And_leaves_a_permissive_services_saturation_forceable()
    {
        await using var db = TestHarness.NewContext("permissive-grid-saturation");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var grid = await new GetStageScheduleQueryHandler(
                db,
                new AcademicYearResolver(db),
                new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new GetStageScheduleQuery(TestHarness.StageId), default);

        grid.Value.Summary.Saturations.Should().ContainSingle().Which.Forceable.Should().BeTrue();
    }

    // ── Authoring it ──────────────────────────────────────────────────────────────

    private static async Task SeedHospitalAsync(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedService(SecondSvcId, "Service existant");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// ⚠ A create that says nothing creates a permissive service. The default is on the command, not
    /// only on the entity, so an API client or a form that has not been taught about the flag cannot
    /// make a service strict by omission — a restriction nobody authored, on the one flag whose whole
    /// purpose is that somebody authored it.
    /// </summary>
    [Fact]
    public async Task Creating_a_service_without_mentioning_the_flag_allows_the_override()
    {
        await using var db = TestHarness.NewContext("firm-create-default");
        await SeedHospitalAsync(db);

        var result = await new CreateServiceCommandHandler(db, new ServiceLevelCapacityResolver(db))
            .Handle(new CreateServiceCommand(
                TestHarness.HospitalId, "Néphrologie", ServiceType.Medical, 20, "Desc", null), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Services.FirstAsync(s => s.Id == result.Value)).AllowsOverCapacity.Should().BeTrue();
    }

    [Fact]
    public async Task A_service_can_be_made_firm_and_opened_again()
    {
        await using var db = TestHarness.NewContext("firm-update-round-trip");
        db.SeedCatalog();
        db.SeedService(ServiceId, "Cardiologie");
        await db.SaveChangesAsync();

        var handler = new UpdateServiceCommandHandler(db, new ServiceLevelCapacityResolver(db));

        UpdateServiceCommand Command(bool allows) => new(
            ServiceId, "Cardiologie", "Desc", ServiceType.Medical, 20, TestHarness.HospitalId, null,
            AllowsOverCapacity: allows);

        (await handler.Handle(Command(false), default)).IsSuccess.Should().BeTrue();
        (await db.Services.FirstAsync(s => s.Id == ServiceId)).AllowsOverCapacity.Should().BeFalse();

        (await handler.Handle(Command(true), default)).IsSuccess.Should().BeTrue();
        (await db.Services.FirstAsync(s => s.Id == ServiceId)).AllowsOverCapacity.Should().BeTrue(
            "a chef changing his mind must not need a migration");
    }

    /// <summary>
    /// ⚠ The fiche carries it because the fiche is what feeds the edit form, and a form saving back a
    /// field the response never sent is how a description column got erased once.
    /// </summary>
    [Fact]
    public async Task The_service_fiche_states_whether_the_override_is_allowed()
    {
        await using var db = TestHarness.NewContext("firm-detail-response");
        db.SeedCatalog();
        db.SeedService(ServiceId, "Cardiologie").AllowsOverCapacity = false;
        await db.SaveChangesAsync();

        var detail = await new GetServiceByIdQueryHandler(db, new ServiceChefProvider(db))
            .Handle(new GetServiceByIdQuery(ServiceId), default);

        detail.IsSuccess.Should().BeTrue();
        detail.Value.AllowsOverCapacity.Should().BeFalse();
    }
}
