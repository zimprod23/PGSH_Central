using FluentAssertions;
using PGSH.Application.AcademicGroups.Placements;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Quel groupe va déjà là où cet étudiant doit aller ? »
///
/// <para>The read exists to make the cheapest answer to a placement request reachable. « Sbai fait
/// tous ses stages à l'hôpital militaire » costs nothing when a roster already goes there — one
/// transfer, no pinned cell, no roster of one student and no dent in the arranger's service
/// balance. Measured on the imported history, 2024-2025 6ᵉ année Médecine held five such rosters of
/// 6-7 students, so this is how the faculty already solves it; what was missing was any way to
/// <i>find</i> them.</para>
/// </summary>
public class RosterPlacementTests
{
    private const int Militaire = 2;
    private const int ChirurgieId = 2;

    private const int CivilCardio = 1;
    private const int MilitaireCardio = 2;
    private const int CivilChirurgie = 3;
    private const int MilitaireChirurgie = 4;

    private const int MilitaryRoster = 1;
    private const int MixedRoster = 2;
    private const int CivilRoster = 3;
    private const int UnarrangedRoster = 4;

    private static GetRosterPlacementsQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static GetRosterPlacementsQuery Query(
        int? serviceId = null, int? hospitalId = null, int? stageId = null,
        PlacementMatch match = PlacementMatch.Anywhere) =>
        new(TestHarness.LevelId, TestHarness.CurrentYearId, stageId, serviceId, hospitalId, match);

    /// <summary>
    /// One promotion, two stages, two hospitals, and the four states a roster can be in relative to
    /// the military hospital: entirely there, partly there, not there, and <b>not arranged at all</b>.
    /// The last is the one every assertion below has to keep out of the answers.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var cardio = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");

        db.SeedHospital(Militaire, "Hôpital Militaire Mohammed V");

        var civilCardioService = db.SeedService(CivilCardio, "Cardiologie");
        var militaryCardioService = db.SeedService(
            MilitaireCardio, "Cardiologie (militaire)", hospitalId: Militaire);
        var civilChirService = db.SeedService(CivilChirurgie, "Chirurgie");
        var militaryChirService = db.SeedService(
            MilitaireChirurgie, "Chirurgie (militaire)", hospitalId: Militaire);

        // Cardiologie runs over two columns, Chirurgie over one — so a run held in a single service
        // has something to fold, and the response can be checked for folding it.
        var cardioP1 = db.SeedSlot(cardio, 1, 1, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        var cardioP2 = db.SeedSlot(cardio, 2, 2, new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 30));
        var chirP1 = db.SeedSlot(chirurgie, 3, 1, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31));

        var military = db.SeedGroup(MilitaryRoster, MilitaryRoster, rotationGroup: "A");
        var mixed = db.SeedGroup(MixedRoster, MixedRoster, rotationGroup: "A");
        var civil = db.SeedGroup(CivilRoster, CivilRoster, rotationGroup: "B");
        var unarranged = db.SeedGroup(UnarrangedRoster, UnarrangedRoster, rotationGroup: "B");

        db.SeedRegistration("Adam", "Sbai", military);
        db.SeedRegistration("Aya", "Ait Chelg", mixed);
        db.SeedRegistration("Rihab", "El Ktaibi", mixed);

        var militaryCardio = db.SeedCohortFor(cardio, military, 11);
        var militaryChir = db.SeedCohortFor(chirurgie, military, 12);
        var mixedCardio = db.SeedCohortFor(cardio, mixed, 21);
        var mixedChir = db.SeedCohortFor(chirurgie, mixed, 22);
        var civilCardio = db.SeedCohortFor(cardio, civil, 31);
        var civilChir = db.SeedCohortFor(chirurgie, civil, 32);

        // The unarranged roster holds its cohortes like everyone else and simply has no cell. That is
        // what makes it indistinguishable from a placed roster on every count but the cells.
        db.SeedCohortFor(cardio, unarranged, 41);
        db.SeedCohortFor(chirurgie, unarranged, 42);

        db.SeedSlotAssignment(1, militaryCardio, cardioP1, militaryCardioService);
        db.SeedSlotAssignment(2, militaryCardio, cardioP2, militaryCardioService);
        db.SeedSlotAssignment(3, militaryChir, chirP1, militaryChirService);

        db.SeedSlotAssignment(4, mixedCardio, cardioP1, militaryCardioService);
        db.SeedSlotAssignment(5, mixedCardio, cardioP2, civilCardioService);
        db.SeedSlotAssignment(6, mixedChir, chirP1, civilChirService);

        db.SeedSlotAssignment(7, civilCardio, cardioP1, civilCardioService);
        db.SeedSlotAssignment(8, civilCardio, cardioP2, civilCardioService);
        db.SeedSlotAssignment(9, civilChir, chirP1, civilChirService);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// ⚠ <b>The test the whole feature turns on.</b> « Tout au militaire » must return the one roster
    /// that really is, and above all must <i>not</i> return the roster nobody has arranged — for which
    /// « aucune cellule ailleurs » is vacuously true. Without the « at least one cell » half of
    /// <see cref="PlacementMatch.Exclusively"/> the unarranged roster is an exact match, and on the
    /// live base (0 cells anywhere) so is every roster in the faculty.
    /// </summary>
    [Fact]
    public async Task Exclusively_at_a_hospital_finds_the_roster_that_is_and_not_the_one_nobody_arranged()
    {
        await using var db = TestHarness.NewContext(nameof(
            Exclusively_at_a_hospital_finds_the_roster_that_is_and_not_the_one_nobody_arranged));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            Query(hospitalId: Militaire, match: PlacementMatch.Exclusively), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rosters.Items.Select(r => r.GroupId).Should().Equal(MilitaryRoster);
        result.Value.Summary.MatchedRosters.Should().Be(1);

        var roster = result.Value.Rosters.Items.Single();
        roster.HospitalPlacement.Should().Be(RosterHospitalPlacement.Entire);
        roster.MatchedStageCount.Should().Be(2);
        roster.StudentCount.Should().Be(1);
    }

    /// <summary>
    /// The weaker question returns the mixed roster too — « il y va aussi » is a real answer, it is
    /// simply not the answer « tout au militaire » asks for. A single enum separating the two is what
    /// stops one being silently served for the other.
    /// </summary>
    [Fact]
    public async Task Anywhere_at_a_hospital_also_finds_the_roster_that_only_partly_is()
    {
        await using var db = TestHarness.NewContext(nameof(
            Anywhere_at_a_hospital_also_finds_the_roster_that_only_partly_is));
        await SeedAsync(db);

        var result = await Handler(db).Handle(Query(hospitalId: Militaire), default);

        result.Value.Rosters.Items.Select(r => r.GroupId)
            .Should().Equal(MilitaryRoster, MixedRoster);

        result.Value.Rosters.Items
            .Single(r => r.GroupId == MixedRoster).HospitalPlacement
            .Should().Be(RosterHospitalPlacement.Partial);
    }

    /// <summary>
    /// The pair request — « stage A en S1, stage B en S2 » — is asked one stage at a time, because
    /// that is what the faculty states. Here: who does Chirurgie in the civil service?
    /// </summary>
    [Fact]
    public async Task A_service_can_be_searched_within_one_stage()
    {
        await using var db = TestHarness.NewContext(nameof(A_service_can_be_searched_within_one_stage));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            Query(serviceId: CivilChirurgie, stageId: ChirurgieId), default);

        result.Value.Rosters.Items.Select(r => r.GroupId).Should().Equal(MixedRoster, CivilRoster);

        // Scoped to one stage, the rows describe that stage only — the other one is not the question.
        result.Value.Rosters.Items.Should().OnlyContain(r => r.Stages.Count == 1);
        result.Value.Rosters.Items.Should().OnlyContain(r => r.Stages[0].StageId == ChirurgieId);
    }

    /// <summary>
    /// A run held in one service is one entry carrying its créneaux, not one entry per column —
    /// the fold <c>SchedulePublisher</c> performs when it publishes, stated on the read side.
    /// </summary>
    [Fact]
    public async Task Cells_of_one_stage_fold_to_one_entry_per_service()
    {
        await using var db = TestHarness.NewContext(nameof(Cells_of_one_stage_fold_to_one_entry_per_service));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            Query(hospitalId: Militaire, match: PlacementMatch.Exclusively), default);

        var cardio = result.Value.Rosters.Items
            .Single().Stages.Single(s => s.StageId == TestHarness.StageId);

        cardio.Services.Should().HaveCount(1);
        cardio.Services[0].ServiceId.Should().Be(MilitaireCardio);
        cardio.Services[0].PeriodNumbers.Should().Equal(1, 2);
        cardio.Services[0].HospitalId.Should().Be(Militaire);

        // The mixed roster's same stage is two services, so the fold is not merging on the stage.
        var mixedResult = await Handler(db).Handle(Query(hospitalId: Militaire), default);
        mixedResult.Value.Rosters.Items
            .Single(r => r.GroupId == MixedRoster)
            .Stages.Single(s => s.StageId == TestHarness.StageId)
            .Services.Should().HaveCount(2);
    }

    /// <summary>
    /// ⚠ <b>What an empty answer means.</b> « Aucun groupe ne va là » and « rien n'est encore
    /// réparti » call for opposite acts, and a bare zero is read as the first. <c>PlacedRosters</c> is
    /// the number that separates them — and on the live base, which holds no cell at all, it is the
    /// answer this read gives today.
    /// </summary>
    [Fact]
    public async Task An_empty_answer_says_whether_anything_is_arranged_at_all()
    {
        await using var db = TestHarness.NewContext(nameof(
            An_empty_answer_says_whether_anything_is_arranged_at_all));

        db.SeedCatalog();
        db.SeedHospital(Militaire, "Hôpital Militaire Mohammed V");
        var group = db.SeedGroup(MilitaryRoster, MilitaryRoster);
        db.SeedCohortFor(db.Stages.Local.First(), group, 11);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            Query(hospitalId: Militaire, match: PlacementMatch.Exclusively), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rosters.Items.Should().BeEmpty();
        result.Value.Summary.PromotionRosters.Should().Be(1);
        result.Value.Summary.PlacedRosters.Should().Be(0, "nothing is arranged — that is not the same "
            + "fact as a hospital nobody sends students to");
        result.Value.Summary.PromotionStages.Should().Be(1);
    }

    /// <summary>
    /// With no service and no hospital named the read is a browse over the promotion, and
    /// <c>Matches</c> is null rather than false: « aucun critère » and « ne correspond pas » are
    /// different answers, and a bool can only give the second.
    /// </summary>
    [Fact]
    public async Task Without_a_target_every_roster_comes_back_and_nothing_claims_to_match()
    {
        await using var db = TestHarness.NewContext(nameof(
            Without_a_target_every_roster_comes_back_and_nothing_claims_to_match));
        await SeedAsync(db);

        var result = await Handler(db).Handle(Query(), default);

        result.Value.Rosters.Items.Should().HaveCount(4);
        result.Value.Rosters.Items.Should().OnlyContain(r => r.HospitalPlacement == null);
        result.Value.Rosters.Items.SelectMany(r => r.Stages)
            .Should().OnlyContain(s => s.Matches == null);
        result.Value.Rosters.Items.Should().OnlyContain(r => r.MatchedStageCount == 0);

        result.Value.Summary.PromotionRosters.Should().Be(4);
        result.Value.Summary.PlacedRosters.Should().Be(3);
        result.Value.Summary.PromotionStages.Should().Be(2);
    }

    /// <summary>
    /// A roster holding a cohorte with no cell keeps its stage in the answer, with an empty service
    /// list. Dropping the stage would make « ce stage reste à répartir pour ce groupe » — a fact
    /// somebody has to act on — indistinguishable from a stage the roster does not owe.
    /// </summary>
    [Fact]
    public async Task A_stage_with_no_cell_is_listed_empty_rather_than_dropped()
    {
        await using var db = TestHarness.NewContext(nameof(
            A_stage_with_no_cell_is_listed_empty_rather_than_dropped));
        await SeedAsync(db);

        var result = await Handler(db).Handle(Query(), default);
        var unarranged = result.Value.Rosters.Items.Single(r => r.GroupId == UnarrangedRoster);

        unarranged.StageCount.Should().Be(2);
        unarranged.PlacedStageCount.Should().Be(0);
        unarranged.Stages.Should().OnlyContain(s => s.Services.Count == 0);
    }

    /// <summary>
    /// ⚠ <b>The anti-drift check.</b> The match is stated twice — once as a SQL predicate choosing
    /// which rosters come back, once in memory as <c>Matches</c> and
    /// <see cref="RosterHospitalPlacement"/> — because EF needs the comparison inline inside its
    /// nested <c>Any</c> and a composed expression would not translate. Nothing else holds the two
    /// together, so three properties are asserted that only hold if they agree:
    ///
    /// <list type="number">
    ///   <item>every returned roster carries a reason for being there (<c>MatchedStageCount &gt; 0</c>);</item>
    ///   <item><c>Exclusively</c> is strictly narrower than <c>Anywhere</c> — it can never return a
    ///   roster the weaker predicate rejects;</item>
    ///   <item>inside the <c>Anywhere</c> result, the rosters the <i>in-memory</i> classifier calls
    ///   <see cref="RosterHospitalPlacement.Entire"/> are exactly the ones the <i>SQL</i> predicate
    ///   returns for <c>Exclusively</c>.</item>
    /// </list>
    ///
    /// The third is the equivalence itself: two independent computations over the same rosters, one
    /// an <c>EXISTS</c>/<c>NOT EXISTS</c> pair in the store and one a pair of counts in memory.
    /// </summary>
    [Fact]
    public async Task The_sql_predicate_and_the_in_memory_verdict_agree()
    {
        await using var db = TestHarness.NewContext(nameof(
            The_sql_predicate_and_the_in_memory_verdict_agree));
        await SeedAsync(db);

        var anywhere = await Handler(db).Handle(Query(hospitalId: Militaire), default);
        var exclusively = await Handler(db).Handle(
            Query(hospitalId: Militaire, match: PlacementMatch.Exclusively), default);

        anywhere.Value.Rosters.Items.Should().NotBeEmpty();
        anywhere.Value.Rosters.Items.Should().OnlyContain(r => r.MatchedStageCount > 0);
        exclusively.Value.Rosters.Items.Should().NotBeEmpty();
        exclusively.Value.Rosters.Items.Should().OnlyContain(r => r.MatchedStageCount > 0);

        var loose = anywhere.Value.Rosters.Items.Select(r => r.GroupId).ToHashSet();
        var strict = exclusively.Value.Rosters.Items.Select(r => r.GroupId).ToList();

        strict.Should().OnlyContain(id => loose.Contains(id),
            "Exclusively is Anywhere plus a condition, so it can never widen the result");

        anywhere.Value.Rosters.Items
            .Where(r => r.HospitalPlacement == RosterHospitalPlacement.Entire)
            .Select(r => r.GroupId)
            .Should().Equal(strict,
                "the in-memory classifier and the SQL predicate are two statements of one rule");

        // And the rosters the weaker predicate already rejects are never « entirely » anywhere: the
        // classifier must not call an absence of evidence a perfect match.
        anywhere.Value.Rosters.Items.Should().NotContain(
            r => r.HospitalPlacement == RosterHospitalPlacement.Unplaced);
    }

    /// <summary>
    /// The promotion is (année, niveau), and both halves are guards rather than filters. Another
    /// year's roster and another promotion's roster are invisible; so is « Non réparti », which
    /// belongs to no promotion and carries a null level — excluded by construction, not by a case.
    /// </summary>
    [Fact]
    public async Task The_promotion_is_the_boundary_and_the_unassigned_bucket_is_never_in_it()
    {
        await using var db = TestHarness.NewContext(nameof(
            The_promotion_is_the_boundary_and_the_unassigned_bucket_is_never_in_it));
        await SeedAsync(db);

        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedGroup(50, 50, academicYearId: TestHarness.PreviousYearId);

        var otherPromotion = db.SeedGroup(60, 60);
        otherPromotion.LevelId = 99;

        db.AcademicGroups.Add(new AcademicGroup
        {
            Id = 999, Label = "Non réparti", GroupNumber = 0,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = null,
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query(), default);

        result.Value.Rosters.Items.Select(r => r.GroupId)
            .Should().Equal(MilitaryRoster, MixedRoster, CivilRoster, UnarrangedRoster);
        result.Value.Summary.PromotionRosters.Should().Be(4);
    }

    /// <summary>
    /// An unknown level is refused rather than answered with an empty page. This read's whole job is
    /// to separate two meanings of a blank; a typo silently producing a third would defeat it.
    /// </summary>
    [Fact]
    public async Task An_unknown_promotion_is_refused_not_answered_with_zero()
    {
        await using var db = TestHarness.NewContext(nameof(
            An_unknown_promotion_is_refused_not_answered_with_zero));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetRosterPlacementsQuery(4242, TestHarness.CurrentYearId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
    }

    /// <summary>
    /// ⚠ A page size of 0 means « non précisé », never « une ligne » —
    /// <c>ToPaginatedResponseAsync</c> clamps it <em>upward</em>, so the fallback has to be the
    /// query's own or a promotion of 192 rosters answers with one and says nothing about it.
    /// </summary>
    [Fact]
    public async Task A_zero_page_size_falls_back_to_the_default_rather_than_to_one_row()
    {
        await using var db = TestHarness.NewContext(nameof(
            A_zero_page_size_falls_back_to_the_default_rather_than_to_one_row));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetRosterPlacementsQuery(TestHarness.LevelId, TestHarness.CurrentYearId,
                PageNumber: 0, PageSize: 0), default);

        result.Value.Rosters.PageSize.Should().Be(GetRosterPlacementsQuery.DefaultPageSize);
        result.Value.Rosters.PageNumber.Should().Be(1);
        result.Value.Rosters.Items.Should().HaveCount(4);
    }
}
