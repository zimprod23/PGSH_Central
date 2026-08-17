using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Hospitals.Services;
using PGSH.Application.Hospitals.Services.Create;
using PGSH.Application.Hospitals.Services.GetById;
using PGSH.Application.Hospitals.Services.GetMany;
using PGSH.Application.Hospitals.Services.Update;
using PGSH.Application.Stages.AllowedServices;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A service states what it accepts in exactly one way, and which way depends on whether anyone has
/// restricted it:
/// <list type="bullet">
///   <item>no quotas → <c>Service.Capacity</c>, counted across every promotion at once;</item>
///   <item>quotas → each promotion against its own quota, and <c>Service.Capacity</c> is <b>not
///   consulted at all</b>. A promotion with no quota is not admitted.</item>
/// </list>
///
/// Two load-bearing consequences several tests below exist only to hold in place:
/// <b>restriction is an act</b> — an unrestricted service admits everyone, which is what keeps the
/// 148 imported services plannable with no data entry — and <b>quotas replace the total rather than
/// sitting under it</b>, so a service of 20 granting 10 and 15 will hold 25 and nothing objects.
/// </summary>
public class ServiceLevelCapacityTests
{
    private const int ServiceId    = 1;
    private const int SecondSvcId  = 2;
    private const int CohortId     = 10;
    private const int OtherLevelId = 77;
    private const int OtherStageId = 55;
    private const int OtherCohortId = 20;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    /// <summary>Fills <paramref name="cohort"/> with <paramref name="students"/> registered assignments.</summary>
    private static void Populate(ApplicationDbContext db, Cohort cohort, int students, int levelId)
    {
        for (int i = 0; i < students; i++)
        {
            var registration = db.SeedRegistration($"E{levelId}_{i}", "Test", cohort.AcademicGroup, levelId: levelId);
            db.SeedAssignment(registration, cohort);
        }
    }

    // ── The domain rule ───────────────────────────────────────────────────────────

    [Fact]
    public void A_service_with_no_quotas_admits_every_level_at_its_full_capacity()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 20 };

        service.HasLevelRestrictions.Should().BeFalse();
        service.Admits(TestHarness.LevelId).Should().BeTrue();
        service.Admits(OtherLevelId).Should().BeTrue("no rule has been authored, so nothing is excluded");
        service.CapacityFor(OtherLevelId).Should().Be(20);
    }

    [Fact]
    public void The_first_quota_closes_the_service_to_every_level_without_one()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 20 };

        service.SetLevelCapacity(TestHarness.LevelId, 10);

        service.Admits(TestHarness.LevelId).Should().BeTrue();
        service.CapacityFor(TestHarness.LevelId).Should().Be(10);
        service.Admits(OtherLevelId).Should().BeFalse("restricting for one promotion restricts against the rest");
        service.CapacityFor(OtherLevelId).Should().Be(0);
    }

    [Fact]
    public void A_quota_replaces_the_services_own_capacity_rather_than_being_capped_by_it()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 8 };
        service.SetLevelCapacity(TestHarness.LevelId, 30);

        service.CapacityFor(TestHarness.LevelId).Should().Be(30,
            "once a quota is authored it is the statement of what the service accepts; Capacity is no longer consulted");
    }

    [Fact]
    public void Quotas_are_independent_of_each_other_and_of_the_total()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 20 };
        service.SetLevelCapacity(TestHarness.LevelId, 10);
        service.SetLevelCapacity(OtherLevelId, 15);

        service.CapacityFor(TestHarness.LevelId).Should().Be(10);
        service.CapacityFor(OtherLevelId).Should().Be(15);
    }

    [Fact]
    public void Replacing_the_quotas_with_an_empty_set_reopens_the_service()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 20 };
        service.SetLevelCapacity(TestHarness.LevelId, 10);

        service.ReplaceLevelCapacities([]);

        service.HasLevelRestrictions.Should().BeFalse();
        service.Admits(OtherLevelId).Should().BeTrue("this is the only way back from a mistaken restriction");
    }

    [Fact]
    public void Replacing_the_quotas_drops_the_levels_left_out_and_updates_the_rest()
    {
        var service = new Service { Name = "Cardiologie", Description = "", Capacity = 20 };
        service.SetLevelCapacity(TestHarness.LevelId, 10);
        service.SetLevelCapacity(OtherLevelId, 15);

        service.ReplaceLevelCapacities([(TestHarness.LevelId, 12)]);

        service.LevelCapacities.Should().ContainSingle().Which.Capacity.Should().Be(12);
        service.Admits(OtherLevelId).Should().BeFalse();
    }

    // ── Publishing ────────────────────────────────────────────────────────────────

    /// <summary>One cohort of <paramref name="students"/> routed through P1 in one service.</summary>
    private static async Task<Service> SeedGridAsync(ApplicationDbContext db, int students, int capacity = 20)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = capacity;

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        Populate(db, cohort, students, TestHarness.LevelId);

        await db.SaveChangesAsync();
        return service;
    }

    [Fact]
    public async Task Publishing_within_the_level_quota_succeeds()
    {
        await using var db = TestHarness.NewContext("cap-quota-ok");
        var service = await SeedGridAsync(db, students: 8, capacity: 20);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsSuccess.Should().BeTrue();
        (await db.ServicePeriods.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task Publishing_beyond_the_level_quota_is_refused_even_when_the_service_has_room()
    {
        await using var db = TestHarness.NewContext("cap-quota-exceeded");
        var service = await SeedGridAsync(db, students: 12, capacity: 20);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.LevelCapacityExceeded");
        result.Error.Description.Should().Contain("12").And.Contain("10");
        (await db.ServicePeriods.CountAsync()).Should().Be(0, "the refusal is total, not partial");
    }

    [Fact]
    public async Task Publishing_onto_a_service_that_refuses_the_promotion_is_refused()
    {
        await using var db = TestHarness.NewContext("cap-not-admitted");
        var service = await SeedGridAsync(db, students: 2, capacity: 20);
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);
        db.SeedLevelCapacity(service, OtherLevelId, 15);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.LevelNotAdmitted",
            "a pharmacie-only service is not merely full for médecine — it was never a candidate");
    }

    [Fact]
    public async Task A_service_with_no_quotas_still_publishes_and_reports_only_the_plain_ceiling()
    {
        await using var db = TestHarness.NewContext("cap-unrestricted");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.CapacityExceeded",
            "with no quota authored there is no quota to blame — naming one sends the user hunting for a rule that is not there");
    }

    /// <summary>
    /// The decisive case for the model chosen: quotas <b>replace</b> the service total, so two
    /// promotions each inside their own quota publish even though the bodies exceed the service's
    /// own number. Nothing objects, by design — the quotas are the statement of what it accepts.
    /// </summary>
    [Fact]
    public async Task Two_promotions_each_within_quota_publish_even_past_the_services_own_total()
    {
        await using var db = TestHarness.NewContext("cap-shared-ceiling");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 20;

        db.SeedLevel(OtherLevelId, "1ère année Médecine", 1);
        var otherStage = db.SeedStage(OtherStageId, "Sémiologie", levelId: OtherLevelId);

        db.SeedLevelCapacity(service, TestHarness.LevelId, 15);
        db.SeedLevelCapacity(service, OtherLevelId, 10);

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        Populate(db, cohort, 15, TestHarness.LevelId);

        // Same service, same dates, other promotion — 6 of a quota of 10.
        var otherCohort = db.SeedCohort(otherStage, OtherCohortId, "Groupe 20");
        db.SeedSlotAssignment(2, otherCohort, db.SeedSlot(otherStage, 200, 1, P1Start, P1End), service);
        Populate(db, otherCohort, 6, OtherLevelId);

        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsSuccess.Should().BeTrue(
            "15 ≤ 15 and 6 ≤ 10; the service's own 20 governs nothing once quotas are authored");
        (await db.ServicePeriods.CountAsync()).Should().Be(15);
    }

    [Fact]
    public async Task One_promotions_overflow_does_not_consume_anothers_quota()
    {
        await using var db = TestHarness.NewContext("cap-quotas-are-independent");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 100;

        db.SeedLevel(OtherLevelId, "1ère année Médecine", 1);
        var otherStage = db.SeedStage(OtherStageId, "Sémiologie", levelId: OtherLevelId);

        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        db.SeedLevelCapacity(service, OtherLevelId, 10);

        // 30 third-years already booked — far past their own quota.
        var crowded = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, crowded, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        Populate(db, crowded, 30, TestHarness.LevelId);

        var otherCohort = db.SeedCohort(otherStage, OtherCohortId, "Groupe 20");
        db.SeedSlotAssignment(2, otherCohort, db.SeedSlot(otherStage, 200, 1, P1Start, P1End), service);
        Populate(db, otherCohort, 8, OtherLevelId);

        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(OtherCohortId, allowOverCapacity: false, default);

        result.IsSuccess.Should().BeTrue(
            "8 ≤ 10 for this promotion, and 38 ≤ 100 overall — the other promotion's overflow is its own problem");
    }

    [Fact]
    public async Task AllowOverCapacity_still_bypasses_the_level_quota()
    {
        await using var db = TestHarness.NewContext("cap-quota-override");
        var service = await SeedGridAsync(db, students: 12, capacity: 20);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsSuccess.Should().BeTrue("the override is the admin accepting every capacity rule's breach, not just the ceiling's");
        (await db.ServicePeriods.CountAsync()).Should().Be(12);
    }

    // ── Auto-arrange ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Auto_arrange_never_routes_a_cohort_into_a_service_that_refuses_its_promotion()
    {
        await using var db = TestHarness.NewContext("cap-arrange-filters");
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);

        var open = db.SeedService(ServiceId, "Cardiologie");
        var pharmacieOnly = db.SeedService(SecondSvcId, "Toxicologie");
        db.SeedLevelCapacity(pharmacieOnly, OtherLevelId, 15);

        stage.AllowedServices.Add(open);
        stage.AllowedServices.Add(pharmacieOnly);

        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        Populate(db, cohort, 5, TestHarness.LevelId);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        var cells = await db.CohortSlotAssignments.ToListAsync();
        cells.Should().NotBeEmpty();
        cells.Should().OnlyContain(c => c.ServiceId == ServiceId,
            "placing a médecine cohort in a pharmacie-only service plans something publish would then refuse");
    }

    [Fact]
    public async Task Auto_arrange_reports_when_no_allowed_service_takes_the_promotion()
    {
        await using var db = TestHarness.NewContext("cap-arrange-none-admit");
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);

        var pharmacieOnly = db.SeedService(ServiceId, "Toxicologie");
        db.SeedLevelCapacity(pharmacieOnly, OtherLevelId, 15);
        stage.AllowedServices.Add(pharmacieOnly);

        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        Populate(db, cohort, 5, TestHarness.LevelId);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.NoServicesAdmitLevel",
            "\"no services\" and \"no services for you\" send the user to two different screens");
    }

    [Fact]
    public async Task Auto_arrange_weights_the_rotation_by_the_level_quota_not_the_raw_ceiling()
    {
        await using var db = TestHarness.NewContext("cap-arrange-weights");
        var stage = db.SeedCatalog();

        // Big room, tiny quota for this promotion; small room, generous quota. Weighting by the
        // ceiling would hand the rotation to the big room it cannot actually use.
        var bigButClosed = db.SeedService(ServiceId, "Grand service");
        bigButClosed.Capacity = 100;
        db.SeedLevelCapacity(bigButClosed, TestHarness.LevelId, 5);

        var smallButOpen = db.SeedService(SecondSvcId, "Petit service");
        smallButOpen.Capacity = 40;
        db.SeedLevelCapacity(smallButOpen, TestHarness.LevelId, 40);

        stage.AllowedServices.Add(bigButClosed);
        stage.AllowedServices.Add(smallButOpen);
        db.SeedSlot(stage, 100, 1, P1Start, P1End);

        foreach (int i in Enumerable.Range(1, 4))
        {
            var cohort = db.SeedCohort(stage, CohortId + i, $"Groupe {i}");
            Populate(db, cohort, 10, TestHarness.LevelId);
        }
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        var cells = await db.CohortSlotAssignments.ToListAsync();
        cells.Count(c => c.ServiceId == SecondSvcId).Should()
            .BeGreaterThan(cells.Count(c => c.ServiceId == ServiceId),
                "the service that will actually take 40 of this promotion should carry the rotation");
    }

    // ── Create / update ───────────────────────────────────────────────────────────

    private static CreateServiceCommandHandler CreateHandler(ApplicationDbContext db) =>
        new(db, new ServiceLevelCapacityResolver(db));

    private static UpdateServiceCommandHandler UpdateHandler(ApplicationDbContext db) =>
        new(db, new ServiceLevelCapacityResolver(db));

    private static async Task SeedHospitalAsync(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedService(SecondSvcId, "Service existant");
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Creating_a_service_persists_its_coordinates_and_quotas()
    {
        await using var db = TestHarness.NewContext("cap-create");
        await SeedHospitalAsync(db);

        var result = await CreateHandler(db).Handle(
            new CreateServiceCommand(
                TestHarness.HospitalId, "Néphrologie", ServiceType.Medical, 20, "Desc", "Rein",
                LocalizationX: "-6.84", LocalizationY: "34.02", LocalizationZ: "12",
                LevelCapacities: [new ServiceLevelCapacityRequest(TestHarness.LevelId, 10)]),
            default);

        result.IsSuccess.Should().BeTrue();

        var created = await db.Services
            .Include(s => s.LevelCapacities)
            .FirstAsync(s => s.Id == result.Value);

        created.LocalisationMaps!.x.Should().Be("-6.84");
        created.LocalisationMaps.y.Should().Be("34.02");
        created.LocalisationMaps.z.Should().Be("12");
        created.LevelCapacities.Should().ContainSingle()
            .Which.Should().Match<ServiceLevelCapacity>(c => c.LevelId == TestHarness.LevelId && c.Capacity == 10);
    }

    [Fact]
    public async Task A_quota_above_the_services_own_capacity_is_accepted()
    {
        await using var db = TestHarness.NewContext("cap-create-over-ceiling");
        await SeedHospitalAsync(db);

        var result = await CreateHandler(db).Handle(
            new CreateServiceCommand(
                TestHarness.HospitalId, "Néphrologie", ServiceType.Medical, 10, "Desc", null,
                LevelCapacities: [new ServiceLevelCapacityRequest(TestHarness.LevelId, 30)]),
            default);

        result.IsSuccess.Should().BeTrue(
            "Capacity governs nothing once a quota exists, so a quota above it contradicts nothing");

        var created = await db.Services.Include(s => s.LevelCapacities).FirstAsync(s => s.Id == result.Value);
        created.CapacityFor(TestHarness.LevelId).Should().Be(30);
    }

    [Fact]
    public async Task A_level_named_twice_in_the_quotas_is_refused()
    {
        await using var db = TestHarness.NewContext("cap-create-duplicate");
        await SeedHospitalAsync(db);

        var result = await CreateHandler(db).Handle(
            new CreateServiceCommand(
                TestHarness.HospitalId, "Néphrologie", ServiceType.Medical, 20, "Desc", null,
                LevelCapacities:
                [
                    new ServiceLevelCapacityRequest(TestHarness.LevelId, 10),
                    new ServiceLevelCapacityRequest(TestHarness.LevelId, 12),
                ]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.DuplicateLevelQuota");
    }

    [Fact]
    public async Task A_quota_for_a_level_that_does_not_exist_is_refused()
    {
        await using var db = TestHarness.NewContext("cap-create-unknown-level");
        await SeedHospitalAsync(db);

        var result = await CreateHandler(db).Handle(
            new CreateServiceCommand(
                TestHarness.HospitalId, "Néphrologie", ServiceType.Medical, 20, "Desc", null,
                LevelCapacities: [new ServiceLevelCapacityRequest(4242, 10)]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.UnknownLevel");
    }

    [Fact]
    public async Task Updating_replaces_the_whole_quota_set()
    {
        await using var db = TestHarness.NewContext("cap-update-replaces");
        db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Médecine", 1);
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await UpdateHandler(db).Handle(
            new UpdateServiceCommand(
                ServiceId, "Cardiologie", "Desc", ServiceType.Medical, 20, TestHarness.HospitalId, null,
                LevelCapacities: [new ServiceLevelCapacityRequest(OtherLevelId, 8)]),
            default);

        result.IsSuccess.Should().BeTrue();

        var quotas = await db.ServiceLevelCapacities.Where(c => c.ServiceId == ServiceId).ToListAsync();
        quotas.Should().ContainSingle()
            .Which.Should().Match<ServiceLevelCapacity>(c => c.LevelId == OtherLevelId && c.Capacity == 8);
    }

    [Fact]
    public async Task Updating_with_no_quotas_reopens_a_restricted_service()
    {
        await using var db = TestHarness.NewContext("cap-update-reopens");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await UpdateHandler(db).Handle(
            new UpdateServiceCommand(
                ServiceId, "Cardiologie", "Desc", ServiceType.Medical, 20, TestHarness.HospitalId, null,
                LevelCapacities: []),
            default);

        result.IsSuccess.Should().BeTrue();
        (await db.ServiceLevelCapacities.CountAsync()).Should().Be(0);
    }

    // ── Authorising a service for a stage ─────────────────────────────────────────

    [Fact]
    public async Task A_service_that_refuses_the_stages_promotion_cannot_be_authorised_for_it()
    {
        await using var db = TestHarness.NewContext("cap-allowed-refused");
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);

        var pharmacieOnly = db.SeedService(ServiceId, "Toxicologie");
        db.SeedLevelCapacity(pharmacieOnly, OtherLevelId, 15);
        await db.SaveChangesAsync();

        var result = await new AddAllowedServiceCommandHandler(db)
            .Handle(new AddAllowedServiceCommand(stage.Id, ServiceId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.ServiceDoesNotAdmitStageLevel");
        result.Error.Description.Should().Contain("Pharmacie",
            "a refusal that names the promotions it does take is one the user can act on");

        (await db.Stages.Include(s => s.AllowedServices).FirstAsync()).AllowedServices
            .Should().BeEmpty("the list must only hold services the stage can actually use");
    }

    [Fact]
    public async Task A_service_with_a_quota_for_the_stages_promotion_is_authorised()
    {
        await using var db = TestHarness.NewContext("cap-allowed-admitted");
        var stage = db.SeedCatalog();

        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await new AddAllowedServiceCommandHandler(db)
            .Handle(new AddAllowedServiceCommand(stage.Id, ServiceId), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Stages.Include(s => s.AllowedServices).FirstAsync()).AllowedServices
            .Should().ContainSingle().Which.Id.Should().Be(ServiceId);
    }

    [Fact]
    public async Task An_unrestricted_service_is_authorised_for_any_stage()
    {
        await using var db = TestHarness.NewContext("cap-allowed-open");
        var stage = db.SeedCatalog();
        db.SeedService(ServiceId, "Cardiologie");
        await db.SaveChangesAsync();

        var result = await new AddAllowedServiceCommandHandler(db)
            .Handle(new AddAllowedServiceCommand(stage.Id, ServiceId), default);

        result.IsSuccess.Should().BeTrue("no rules authored means no promotion excluded");
    }

    // ── Reads ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_service_detail_falls_back_to_the_hospitals_coordinates_and_says_so()
    {
        await using var db = TestHarness.NewContext("cap-detail-fallback");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Hospital.LocalisationMaps = new Localization("-6.84", "34.02", null);
        await db.SaveChangesAsync();

        var result = await new GetServiceByIdQueryHandler(db).Handle(new GetServiceByIdQuery(ServiceId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.LocalizationX.Should().Be("-6.84");
        result.Value.HasOwnLocalization.Should().BeFalse(
            "the form must not save an inherited position back as one the service stated");
    }

    [Fact]
    public async Task The_service_detail_prefers_the_services_own_coordinates()
    {
        await using var db = TestHarness.NewContext("cap-detail-own");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Hospital.LocalisationMaps = new Localization("-6.84", "34.02", null);
        service.LocalisationMaps = new Localization("-6.90", "34.10", null);
        await db.SaveChangesAsync();

        var result = await new GetServiceByIdQueryHandler(db).Handle(new GetServiceByIdQuery(ServiceId), default);

        result.Value.LocalizationX.Should().Be("-6.90");
        result.Value.HasOwnLocalization.Should().BeTrue();
    }

    [Fact]
    public async Task Filtering_by_admitted_level_keeps_unrestricted_services()
    {
        await using var db = TestHarness.NewContext("cap-filter-admits");
        db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);

        db.SeedService(ServiceId, "Ouvert à tous");
        var pharmacieOnly = db.SeedService(SecondSvcId, "Toxicologie");
        db.SeedLevelCapacity(pharmacieOnly, OtherLevelId, 15);
        await db.SaveChangesAsync();

        var result = await new GetServicesQueryHandler(db).Handle(
            new GetServicesQuery(AdmitsLevelId: TestHarness.LevelId, PageSize: 50), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Select(i => i.Id).Should().BeEquivalentTo([ServiceId],
            "an unrestricted service takes all comers; a pharmacie-only one does not");
    }

    [Fact]
    public async Task The_service_list_reports_how_many_promotions_a_service_is_restricted_to()
    {
        await using var db = TestHarness.NewContext("cap-list-count");
        db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "1ère année Médecine", 1);
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        db.SeedLevelCapacity(service, OtherLevelId, 5);
        await db.SaveChangesAsync();

        var result = await new GetServicesQueryHandler(db).Handle(new GetServicesQuery(PageSize: 50), default);

        result.Value.Items.Single().RestrictedLevelCount.Should().Be(2);
    }

    [Fact]
    public async Task The_schedule_grid_reports_both_ceilings_for_a_cell()
    {
        await using var db = TestHarness.NewContext("cap-grid-cell");
        var service = await SeedGridAsync(db, students: 8, capacity: 20);
        db.SeedLevelCapacity(service, TestHarness.LevelId, 10);
        await db.SaveChangesAsync();

        var result = await new PGSH.Application.Stages.Schedule.GetStageScheduleQueryHandler(
                db,
                new PGSH.Application.AcademicYears.AcademicYearResolver(db),
                new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new PGSH.Application.Stages.Schedule.GetStageScheduleQuery(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        var cell = result.Value.Cohorts.Single().Cells.Single()!;
        cell.Capacity.Should().Be(10, "the quota governs, not the service's 20");
        cell.OccupiedSeats.Should().Be(8);
        cell.IsLevelQuota.Should().BeTrue();
        cell.AdmitsLevel.Should().BeTrue();
    }

    [Fact]
    public async Task The_schedule_grid_reports_the_service_total_when_no_quota_is_configured()
    {
        await using var db = TestHarness.NewContext("cap-grid-cell-open");
        await SeedGridAsync(db, students: 8, capacity: 20);

        var result = await new PGSH.Application.Stages.Schedule.GetStageScheduleQueryHandler(
                db,
                new PGSH.Application.AcademicYears.AcademicYearResolver(db),
                new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new PGSH.Application.Stages.Schedule.GetStageScheduleQuery(TestHarness.StageId), default);

        var cell = result.Value.Cohorts.Single().Cells.Single()!;
        cell.Capacity.Should().Be(20);
        cell.OccupiedSeats.Should().Be(8);
        cell.IsLevelQuota.Should().BeFalse();
    }
}
