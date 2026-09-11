using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Services.PromotionFit;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Cette promotion tient-elle ? » — the read that answers before anything is planned.
///
/// <para>⚠ <b>Why it exists.</b> <c>OccupancyReport</c> measures placed cells, so a promotion that
/// has not been cut yet reads as <b>zero pressure</b> — comfortably empty — right up to the moment
/// somebody presses « Publier » and the shortfall appears. On the live base that is how the 3ᵉ MED
/// came to be published with two Dermatologie services holding 27-30 students for a ceiling of 20,
/// eight times over with « autoriser le dépassement » ticked.</para>
///
/// <para>⚠ <b>The first test is the calibration and it is the point of the file.</b> The formula is
/// only worth showing if it reproduces what was measured by hand on the real base (sessions 61-62):
/// Dermatologie <b>−14</b> and Santé Publique <b>+6</b> for the 3ᵉ MED. A formula that agreed with
/// nothing would be a second opinion nobody could act on.</para>
/// </summary>
public class PromotionFitTests
{
    private const int MedLevel = 1;      // TestHarness.LevelId — 3ème année Médecine
    private const int PharmaLevel = 7;

    private static GetPromotionFitQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static Service Open(ApplicationDbContext db, int id, string name, int capacity)
    {
        var service = db.SeedService(id, name);
        service.Capacity = capacity;
        return service;
    }

    /// <summary>
    /// The 3ᵉ MED as the live base holds it: 933 students, eight stages — Médecine and Chirurgie at
    /// 30 days, six others at 15 — which is an axis of <b>ten columns of 15 days</b>.
    /// </summary>
    /// <remarks>
    /// The students are seeded as a count rather than one by one: 933 registrations is the fact under
    /// test and a loop of 933 inserts would make the file slow for nothing.
    /// </remarks>
    private static void SeedMed3(ApplicationDbContext db, int students = 933)
    {
        db.SeedCatalog();

        var cardiologie = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        cardiologie.Name = "Médecine";
        cardiologie.DurationInDays = 30;

        db.SeedStage(2, "Chirurgie").DurationInDays = 30;

        foreach (var (id, name) in new[]
                 {
                     (3, "Dermatologie - Endocrinologie"), (4, "Santé Publique"),
                     (5, "Simulation Médicale"), (6, "Pédiatrie"),
                     (7, "Gynécologie"), (8, "Urgences"),
                 })
        {
            db.SeedStage(id, name).DurationInDays = 15;
        }

        for (int i = 0; i < students; i++)
            db.SeedRegistration($"E{i}", "Test", levelId: MedLevel);
    }

    private static PromotionFitStageRow StageRow(PromotionFitResponse response, string stageName) =>
        response.Promotions
            .SelectMany(p => p.Stages)
            .First(s => s.StageName == stageName);

    /// <summary>
    /// ⚠ <b>The calibration.</b> 933 students over ten columns is 94 standing in any 15-day stage at
    /// once. Dermatologie authorises four services of 20 → 80 places → <b>−14</b>. Santé Publique
    /// authorises five → 100 → <b>+6</b>. Both were measured by hand on the live base before this
    /// read existed.
    /// </summary>
    [Fact]
    public async Task The_formula_reproduces_what_was_measured_by_hand()
    {
        await using var db = TestHarness.NewContext("fit-calibration");
        SeedMed3(db);

        var derma = db.Stages.Local.First(s => s.Name == "Dermatologie - Endocrinologie");
        var sante = db.Stages.Local.First(s => s.Name == "Santé Publique");

        db.AllowInOrder(derma,
            Open(db, 12, "Dermato 1", 20), Open(db, 13, "Dermato 2", 20),
            Open(db, 127, "Dermato 3", 20), Open(db, 135, "Dermato 4", 20));

        db.AllowInOrder(sante,
            Open(db, 20, "SP 1", 20), Open(db, 21, "SP 2", 20), Open(db, 22, "SP 3", 20),
            Open(db, 23, "SP 4", 20), Open(db, 24, "SP 5", 20));

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var promotion = result.Value.Promotions.Should().ContainSingle().Subject;

        promotion.Students.Should().Be(933);
        promotion.Timeline.Should().Be(10, "2 + 2 + six ones — T = Σkₛ");
        promotion.ColumnDays.Should().Be(15, "the gcd of 30 and 15 is what one column lasts");

        var dermaRow = StageRow(result.Value, "Dermatologie - Endocrinologie");
        dermaRow.Periods.Should().Be(1);
        dermaRow.StudentsAtOnce.Should().Be(94, "933 ÷ 10 rounded up — the remainder is a real student");
        dermaRow.Places.Should().Be(80);
        dermaRow.Margin.Should().Be(-14, "the number measured on the live base on 11/09/2026");
        dermaRow.State.Should().Be(StageFitState.OverCapacity);

        var santeRow = StageRow(result.Value, "Santé Publique");
        santeRow.Margin.Should().Be(6, "and the other number measured the same day");
        santeRow.State.Should().Be(StageFitState.Fits);

        // A two-column stage carries twice the slice: 933 × 2 ÷ 10.
        StageRow(result.Value, "Chirurgie").StudentsAtOnce.Should().Be(187);
    }

    /// <summary>
    /// ⚠ <b>The three ways a stage is unplaceable are not one way.</b> « aucun service autorisé »,
    /// « aucun n'admet cette promotion » and « tous réservés » call for three different acts — author
    /// the list, grant a quota, release the reservation — and the arranger itself refuses with three
    /// different errors. A panel printing « 0 place » for all three would send the reader to the
    /// wrong screen twice out of three.
    /// </summary>
    [Fact]
    public async Task The_three_unplaceable_states_are_told_apart()
    {
        await using var db = TestHarness.NewContext("fit-unplaceable");
        SeedMed3(db, students: 100);

        var none = db.Stages.Local.First(s => s.Name == "Pédiatrie");

        var reservedOnly = db.Stages.Local.First(s => s.Name == "Urgences");
        db.AllowInOrder(reservedOnly, Open(db, 30, "Urgences A", 20));
        db.Reserve(reservedOnly, db.Services.Local.First(s => s.Id == 30));

        var admitsNobody = db.Stages.Local.First(s => s.Name == "Gynécologie");
        var restricted = Open(db, 31, "Gynéco A", 20);
        db.SeedLevelCapacity(restricted, PharmaLevel, 10);
        db.AllowInOrder(admitsNobody, restricted);

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(), default);

        StageRow(result.Value, none.Name).State.Should().Be(StageFitState.NoAllowedServices);
        StageRow(result.Value, reservedOnly.Name).State.Should().Be(StageFitState.AllServicesReserved);
        StageRow(result.Value, admitsNobody.Name).State.Should().Be(StageFitState.NoServiceAdmits);

        var promotion = result.Value.Promotions.Should().ContainSingle().Subject;
        promotion.State.Should().Be(PromotionFitState.Unplaceable,
            "« impossible » outranks « en dépassement » — one is a catalogue gap, the other a decision");

        result.Value.Totals.StagesUnplaceable.Should().Be(8,
            "the six stages nobody authorised at all are unplaceable for the same reason");
        result.Value.Notes.Should().Contain(n => n.Contains("aucun service que la rotation puisse tirer"));
    }

    /// <summary>
    /// ⚠ A reserved service's places leave the total with it — that is the whole meaning of holding
    /// one back for named rosters — and the count travels so « il manque N places » can be read
    /// against « et j'en ai mis M de côté ».
    /// </summary>
    [Fact]
    public async Task A_reserved_service_contributes_no_places_and_says_so()
    {
        await using var db = TestHarness.NewContext("fit-reserved");
        SeedMed3(db, students: 100);

        var stage = db.Stages.Local.First(s => s.Name == "Pédiatrie");
        var open = Open(db, 40, "Pédiatrie A", 20);
        var held = Open(db, 41, "Pédiatrie B", 20);
        db.AllowInOrder(stage, open, held);
        db.Reserve(stage, held);

        await db.SaveChangesAsync();

        var row = StageRow((await Handler(db).Handle(new GetPromotionFitQuery(), default)).Value, "Pédiatrie");

        row.AllowedServices.Should().Be(2);
        row.UsableServices.Should().Be(1);
        row.ReservedServices.Should().Be(1);
        row.Places.Should().Be(20, "the held service's 20 left with it");
    }

    /// <summary>
    /// ⚠ A quota <b>replaces</b> the service's capacity rather than sitting under it, and the panel
    /// asks the domain rather than re-deriving the rule: a service of 20 granting this promotion 5
    /// offers 5, and one granting it nothing offers nothing.
    /// </summary>
    [Fact]
    public async Task A_quota_replaces_the_capacity_it_does_not_cap_it()
    {
        await using var db = TestHarness.NewContext("fit-quota");
        SeedMed3(db, students: 100);

        var stage = db.Stages.Local.First(s => s.Name == "Pédiatrie");
        var quota = Open(db, 50, "Pédiatrie A", 20);
        db.SeedLevelCapacity(quota, MedLevel, 5);
        db.AllowInOrder(stage, quota);

        await db.SaveChangesAsync();

        var row = StageRow((await Handler(db).Handle(new GetPromotionFitQuery(), default)).Value, "Pédiatrie");

        row.Places.Should().Be(5, "the quota is the one limit in force; Capacity is not consulted");
        row.Services.Should().ContainSingle().Which.Admits.Should().BeTrue();
    }

    /// <summary>
    /// ⚠ <b>A frozen registration is not a student the cut will place.</b> Counted into the headcount
    /// it would inflate every share; dropped silently it would make a promotion of frozen students
    /// read as one that comfortably fits. Excluded, counted, and named in the notes.
    /// </summary>
    [Fact]
    public async Task Held_registrations_are_excluded_and_reported()
    {
        await using var db = TestHarness.NewContext("fit-held");
        SeedMed3(db, students: 10);

        var frozen = db.SeedRegistration("Gelé", "Test", levelId: MedLevel);
        frozen.Holds.Add(new RegistrationHold
        {
            Id = Guid.NewGuid(),
            RegistrationId = frozen.Id,
            // ⚠ A *blocking* reason: « dossier à compléter » flags a file without withdrawing the
            // student from planning, so seeding that one would assert nothing here.
            Reason = RegistrationHoldReason.AbsentFromReinscriptionRoll,
            Evidence = "absent du rouleau",
            RaisedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        });

        await db.SaveChangesAsync();

        var promotion = (await Handler(db).Handle(new GetPromotionFitQuery(), default))
            .Value.Promotions.Should().ContainSingle().Subject;

        promotion.Students.Should().Be(10, "the frozen one is not a student the cut will reach");
        promotion.HeldStudents.Should().Be(1);
    }

    /// <summary>
    /// ⚠ <b>The caveat the panel cannot compute away.</b> Two promotions authorising the same service
    /// are each told they fit, because nothing here knows when either passes through — and the
    /// arranger will not see it either, since it weights by capacity and never reads live occupancy.
    /// A filter on one promotion must not hide the other: the sharing is a fact about the service.
    /// </summary>
    [Fact]
    public async Task A_service_two_promotions_authorise_is_named_on_both_even_under_a_filter()
    {
        await using var db = TestHarness.NewContext("fit-shared");
        SeedMed3(db, students: 100);

        db.SeedLevel(PharmaLevel, "2ème année Pharmacie", 2, AcademicProgram.Pharmacie);
        var pharmaStage = db.SeedStage(60, "Pharmacie Clinique", levelId: PharmaLevel);
        pharmaStage.DurationInDays = 15;
        db.SeedRegistration("Pharma", "Test", levelId: PharmaLevel);

        var shared = Open(db, 70, "Service partagé", 40);
        db.AllowInOrder(db.Stages.Local.First(s => s.Name == "Pédiatrie"), shared);
        db.AllowInOrder(pharmaStage, shared);

        await db.SaveChangesAsync();

        var filtered = await Handler(db).Handle(new GetPromotionFitQuery(LevelId: MedLevel), default);

        filtered.Value.Promotions.Should().ContainSingle("the filter picks what is listed");

        StageRow(filtered.Value, "Pédiatrie").Services.Should().ContainSingle()
            .Which.AlsoAuthorisedBy.Should().ContainSingle().Which.Should().Be("2ème année Pharmacie",
                "a filter never lowers a shared service's claimants");

        filtered.Value.Notes.Should().Contain(n => n.Contains("plusieurs promotions"));
    }

    /// <summary>
    /// A promotion with nothing in the catalogue. ⚠ Reported as its own state, not as one that fits:
    /// on the live base the 7ᵉ MED carries 1 347 registrations and no stage at all, and « ✓ » beside
    /// it would be the most misleading cell on the page.
    /// </summary>
    [Fact]
    public async Task A_promotion_without_stages_is_a_state_of_its_own()
    {
        await using var db = TestHarness.NewContext("fit-no-stages");
        db.SeedCatalog();
        db.SeedLevel(PharmaLevel, "6ème année Pharmacie", 6, AcademicProgram.Pharmacie);
        db.SeedRegistration("Sans", "Stage", levelId: PharmaLevel);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(LevelId: PharmaLevel), default);

        var promotion = result.Value.Promotions.Should().ContainSingle().Subject;
        promotion.State.Should().Be(PromotionFitState.NoStages);
        promotion.Timeline.Should().Be(0);
        result.Value.Notes.Should().Contain(n => n.Contains("aucun stage au catalogue"));
    }

    /// <summary>« Retrait » is a marker wearing a level's clothes: refused, never reported empty.</summary>
    [Fact]
    public async Task Retrait_is_refused_rather_than_reported_empty()
    {
        await using var db = TestHarness.NewContext("fit-retrait");
        db.SeedCatalog();
        db.SeedLevel(99, "Retrait", 0);
        db.SeedRegistration("Parti", "Test", levelId: 99);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(LevelId: 99), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotAPromotion");
    }

    /// <summary>
    /// ⚠ A year nobody is registered in is « rien à lire », not « tout va bien » — and it is a
    /// <c>NotFound</c>, so the sentence reaches the screen instead of a 500.
    /// </summary>
    [Fact]
    public async Task A_year_with_no_registration_says_so()
    {
        await using var db = TestHarness.NewContext("fit-empty-year");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PromotionFit.NoPromotionsInYear");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// ⚠ The year is resolved the usual way — omitted means the current one. A promotion of another
    /// year must not appear in this one's panel, however year-invariant its stages are.
    /// </summary>
    [Fact]
    public async Task Another_years_registrations_are_not_this_years_promotion()
    {
        await using var db = TestHarness.NewContext("fit-year-scope");
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedLevel(PharmaLevel, "2ème année Pharmacie", 2, AcademicProgram.Pharmacie);
        db.SeedRegistration("Ancien", "Test",
            academicYearId: TestHarness.PreviousYearId, levelId: PharmaLevel);
        db.SeedRegistration("Actuel", "Test", levelId: MedLevel);

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetPromotionFitQuery(), default);

        result.Value.Promotions.Should().ContainSingle()
            .Which.LevelId.Should().Be(MedLevel);
    }
}
