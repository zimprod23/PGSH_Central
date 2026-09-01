using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Stages.Repartition;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The <i>répartition annuelle des stages</i> — the table the faculty publishes, pivoted out of the
/// same <c>CohortSlotAssignment</c> cells the schedule grid holds. Shaped after
/// <c>example_stage_assignement/Med3.png</c> in miniature: one level, two stages, two periods, and
/// eight groups rotating between two services each.
/// </summary>
public class LevelRepartitionTests
{
    private const int ChirurgieId  = 2;
    private const int MedecineA    = 10;
    private const int Nephrologie  = 11;
    private const int ChirurgieA   = 20;
    private const int Traumatologie = 21;

    private static readonly DateOnly P1Start = new(2025, 11, 3);
    private static readonly DateOnly P1End   = new(2025, 12, 17);
    private static readonly DateOnly P2Start = new(2025, 12, 18);
    private static readonly DateOnly P2End   = new(2026, 3, 17);

    private static GetLevelRepartitionQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new ServiceChefProvider(db));

    /// <summary>
    /// Groups 1-4 (partition A) rotate through Médecine, groups 5-8 (partition B) through Chirurgie,
    /// each swapping service between the two periods.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var medecine  = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");

        var medA  = db.SeedService(MedecineA, "Médecine A");
        var nephro = db.SeedService(Nephrologie, "Néphrologie");
        var chirA = db.SeedService(ChirurgieA, "Chirurgie A");
        var trauma = db.SeedService(Traumatologie, "Traumatologie");

        var medP1  = db.SeedSlot(medecine, 1, 1, P1Start, P1End);
        var medP2  = db.SeedSlot(medecine, 2, 2, P2Start, P2End);
        var chirP1 = db.SeedSlot(chirurgie, 3, 1, P1Start, P1End);
        var chirP2 = db.SeedSlot(chirurgie, 4, 2, P2Start, P2End);

        int cellId = 1;
        void Assign(PGSH.Domain.Stages.Cohort cohort, PGSH.Domain.Stages.StageSlot slot, Service service) =>
            db.SeedSlotAssignment(cellId++, cohort, slot, service);

        foreach (int number in new[] { 1, 2, 3, 4 })
        {
            var group  = db.SeedGroup(number, number, rotationGroup: "A");
            var cohort = db.SeedCohortFor(medecine, group, 100 + number);

            Assign(cohort, medP1, number <= 2 ? medA : nephro);
            Assign(cohort, medP2, number <= 2 ? nephro : medA);
        }

        foreach (int number in new[] { 5, 6, 7, 8 })
        {
            var group  = db.SeedGroup(number, number, rotationGroup: "B");
            var cohort = db.SeedCohortFor(chirurgie, group, 200 + number);

            Assign(cohort, chirP1, number <= 6 ? chirA : trauma);
            Assign(cohort, chirP2, number <= 6 ? trauma : chirA);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_level_prints_one_row_per_stage_and_service_with_its_group_ranges()
    {
        await using var db = TestHarness.NewContext("repartition-shape");
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value;

        report.Columns.Should().HaveCount(2);
        report.Columns[0].StartDate.Should().Be(P1Start);
        report.Columns[1].EndDate.Should().Be(P2End);

        report.Rows.Select(r => (r.StageName, r.ServiceName, r.Cells[0]!.Groups, r.Cells[1]!.Groups))
            .Should().Equal(
                ("Cardiologie", "Médecine A",    "1-2", "3-4"),
                ("Cardiologie", "Néphrologie",   "3-4", "1-2"),
                ("Chirurgie",   "Chirurgie A",   "5-6", "7-8"),
                ("Chirurgie",   "Traumatologie", "7-8", "5-6"));

        report.Summary.Should().Be(new RepartitionSummary(
            RowCount: 4, ColumnCount: 2, PlannedCells: 8, EmptyCells: 0, GroupCount: 8,
            DeclaredSlotCount: 4));
    }

    [Fact]
    public async Task Rows_and_stages_are_ordered_by_the_groups_they_open_on()
    {
        // The published document reads top-to-bottom in rotation order: Médecine (groups 1-40) above
        // Chirurgie (41-80) in the 3rd year, and inside each stage 41-43, 44-46, 47-50… Sorting by
        // the first period's lowest group number is what reproduces it — not the stage or service id.
        await using var db = TestHarness.NewContext("repartition-order");
        await SeedAsync(db);

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Select(r => r.Cells[0]!.GroupNumbers[0]).Should().BeInAscendingOrder();
        report.Rows.Select(r => r.StageId).Should().Equal(
            TestHarness.StageId, TestHarness.StageId, ChirurgieId, ChirurgieId);
    }

    [Fact]
    public async Task A_cell_carries_the_partition_that_is_in_it()
    {
        await using var db = TestHarness.NewContext("repartition-band");
        await SeedAsync(db);

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Select(r => r.Cells[0]!.RotationGroup).Should().Equal("A", "A", "B", "B");
    }

    [Fact]
    public async Task A_row_holds_a_different_partition_in_each_period_of_a_crossover()
    {
        // The crossover, which is the whole point of the published document: A takes Médecine while B
        // takes Chirurgie, then they swap. A Médecine row therefore holds A in P1 and B in P2, so there
        // is no such thing as "this row's partition".
        //
        // ⚠ This is the case the old row-level band could not express, and it failed in the worst way:
        // with two partitions every Médecine row opens on A and every Chirurgie row on B, so the
        // document rendered a colour-per-stage under a legend reading « Partition A / Partition B ».
        // Plausible, consistent, and wrong — which is why the fixture here mirrors and SeedAsync's
        // does not.
        await using var db = TestHarness.NewContext("repartition-crossover");
        var medecine  = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");

        var medA  = db.SeedService(MedecineA, "Médecine A");
        var chirA = db.SeedService(ChirurgieA, "Chirurgie A");

        var medP1  = db.SeedSlot(medecine, 1, 1, P1Start, P1End);
        var medP2  = db.SeedSlot(medecine, 2, 2, P2Start, P2End);
        var chirP1 = db.SeedSlot(chirurgie, 3, 1, P1Start, P1End);
        var chirP2 = db.SeedSlot(chirurgie, 4, 2, P2Start, P2End);

        int cellId = 1;

        var a = db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedSlotAssignment(cellId++, db.SeedCohortFor(medecine, a, 101), medP1, medA);
        db.SeedSlotAssignment(cellId++, db.SeedCohortFor(chirurgie, a, 102), chirP2, chirA);

        var b = db.SeedGroup(2, 2, rotationGroup: "B");
        db.SeedSlotAssignment(cellId++, db.SeedCohortFor(chirurgie, b, 103), chirP1, chirA);
        db.SeedSlotAssignment(cellId++, db.SeedCohortFor(medecine, b, 104), medP2, medA);

        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        var medecineRow = report.Rows.Single(r => r.StageId == TestHarness.StageId);
        medecineRow.Cells.Select(c => c!.RotationGroup).Should().Equal("A", "B");

        var chirurgieRow = report.Rows.Single(r => r.StageId == ChirurgieId);
        chirurgieRow.Cells.Select(c => c!.RotationGroup).Should().Equal("B", "A");
    }

    [Fact]
    public async Task A_cell_holding_two_partitions_at_once_carries_neither()
    {
        // Nothing forbids it: a gap-filled promotion can put groups of both partitions into one service
        // for one period. There is no honest colour for that cell, so it gets none rather than the
        // first one found.
        await using var db = TestHarness.NewContext("repartition-mixed-band");
        var medecine = db.SeedCatalog();
        var medA     = db.SeedService(MedecineA, "Médecine A");
        var p1       = db.SeedSlot(medecine, 1, 1, P1Start, P1End);

        var a = db.SeedGroup(1, 1, rotationGroup: "A");
        var b = db.SeedGroup(2, 2, rotationGroup: "B");
        db.SeedSlotAssignment(1, db.SeedCohortFor(medecine, a, 101), p1, medA);
        db.SeedSlotAssignment(2, db.SeedCohortFor(medecine, b, 102), p1, medA);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        var cell = report.Rows.Single().Cells[0]!;
        cell.Groups.Should().Be("1-2");
        cell.RotationGroup.Should().BeNull();
    }

    [Fact]
    public async Task A_period_spanning_two_columns_repeats_its_cell_under_the_same_slot()
    {
        // The 6th-year shape: ANES REA changes service monthly while Chirurgie keeps its groups for
        // two months. The axis is monthly and the two-month stage prints the same cell in both
        // columns — carrying one slot id, so a renderer that prefers to merge them can.
        await using var db = TestHarness.NewContext("repartition-span");

        var monthly   = db.SeedCatalog();
        var bimonthly = db.SeedStage(ChirurgieId, "Chirurgie");

        var rea  = db.SeedService(MedecineA, "Réanimation");
        var chir = db.SeedService(ChirurgieA, "Chirurgie A");

        var m1 = db.SeedSlot(monthly, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 12, 2));
        var m2 = db.SeedSlot(monthly, 2, 2, new DateOnly(2025, 12, 3), new DateOnly(2026, 1, 2));
        var b1 = db.SeedSlot(bimonthly, 3, 1, new DateOnly(2025, 11, 3), new DateOnly(2026, 1, 2));

        var groupOne = db.SeedGroup(1, 1);
        var groupTwo = db.SeedGroup(2, 2);

        var monthlyCohort = db.SeedCohortFor(monthly, groupOne, 101);
        db.SeedSlotAssignment(1, monthlyCohort, m1, rea);
        db.SeedSlotAssignment(2, monthlyCohort, m2, rea);
        db.SeedSlotAssignment(3, db.SeedCohortFor(bimonthly, groupTwo, 202), b1, chir);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Columns.Should().HaveCount(2);

        var chirurgieRow = report.Rows.Single(r => r.StageId == ChirurgieId);
        chirurgieRow.Cells[0]!.Groups.Should().Be("2");
        chirurgieRow.Cells[1]!.Groups.Should().Be("2");
        chirurgieRow.Cells[0]!.SlotId.Should().Be(chirurgieRow.Cells[1]!.SlotId);
    }

    [Fact]
    public async Task Another_years_planning_of_the_same_level_is_not_printed()
    {
        // A level's cohorts exist per year and its slots carry that year's dates. Unscoped, the
        // Chirurgie of six promotions lands in one table.
        await using var db = TestHarness.NewContext("repartition-year");
        await SeedAsync(db);

        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var stage    = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var lastYear = db.SeedSlot(stage, 900, 1,
            new DateOnly(2024, 11, 3), new DateOnly(2024, 12, 17), TestHarness.PreviousYearId);
        var oldGroup = db.SeedGroup(900, 77, academicYearId: TestHarness.PreviousYearId);

        db.SeedSlotAssignment(900, db.SeedCohortFor(stage, oldGroup, 900), lastYear,
            db.Services.Local.First(s => s.Id == MedecineA));
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.AcademicYearLabel.Should().Be("2025-2026");
        report.Columns.Should().HaveCount(2);
        report.Rows.SelectMany(r => r.Cells).Where(c => c is not null)
            .SelectMany(c => c!.GroupNumbers).Should().NotContain(77);
    }

    [Fact]
    public async Task The_chef_printed_is_the_one_in_charge_when_the_planning_starts()
    {
        // A répartition reprinted years later has to keep naming the chef it was published with.
        await using var db = TestHarness.NewContext("repartition-chef");
        await SeedAsync(db);

        var service = db.Services.Local.First(s => s.Id == MedecineA);
        var thenChef = new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Pr.Y.", LastName = "Sekkach", Position = Position.ServiceChef,
        };
        var nowChef = new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Pr.M.", LastName = "Tamzaourt", Position = Position.ServiceChef,
        };
        db.Users.AddRange(thenChef, nowChef);

        // No Id here on purpose: pre-setting a store-generated key on a child of an already-tracked
        // parent makes EF classify it Modified and UPDATE a row that was never inserted.
        service.ChefHistory.Add(new ServiceChefAssignment
        {
            ServiceId = service.Id, EmployeeId = thenChef.Id, Employee = thenChef,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30),
        });
        service.ChefHistory.Add(new ServiceChefAssignment
        {
            ServiceId = service.Id, EmployeeId = nowChef.Id, Employee = nowChef,
            StartDate = new DateOnly(2026, 7, 1),
        });
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Single(r => r.ServiceId == MedecineA).ChefName.Should().Be("Pr.Y. Sekkach");
    }

    [Fact]
    public async Task A_service_with_no_recorded_tenure_falls_back_to_the_sitting_chef()
    {
        // The legacy import carried no tenure trail; printing no name at all would be worse.
        await using var db = TestHarness.NewContext("repartition-chef-fallback");
        var stage = db.SeedCatalog();

        var chef = db.SeedChef(Guid.NewGuid());
        chef.FirstName = "Pr.H.";
        chef.LastName  = "Harmouch";

        var service = db.SeedService(MedecineA, "Médecine A", chef);
        service.ChefHistory.Clear();

        var slot  = db.SeedSlot(stage, 1, 1, P1Start, P1End);
        var group = db.SeedGroup(1, 1);
        db.SeedSlotAssignment(1, db.SeedCohortFor(stage, group, 101), slot, service);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Single().ChefName.Should().Be("Pr.H. Harmouch");
        report.Rows.Single().ChefIsFromSourceNote.Should().BeFalse();
    }

    /// <summary>
    /// The case that actually holds on the imported data: 140 of 148 services name their chef only
    /// in the description, and none has one linked. Reading the structured field alone printed no
    /// name on 95% of the document's rows.
    /// </summary>
    [Fact]
    public async Task A_service_with_no_chef_at_all_falls_back_to_the_legacy_source_note()
    {
        await using var db = TestHarness.NewContext("repartition-chef-source-note");
        var stage = db.SeedCatalog();

        var service = db.SeedService(MedecineA, "Chirurgie B");
        service.ChefHistory.Clear();
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");

        var slot  = db.SeedSlot(stage, 1, 1, P1Start, P1End);
        var group = db.SeedGroup(1, 1);
        db.SeedSlotAssignment(1, db.SeedCohortFor(stage, group, 101), slot, service);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        var row = report.Rows.Single();
        row.ChefName.Should().Be("Pr.A.Settaf");
        row.ChefIsFromSourceNote.Should().BeTrue(
            "an undated note is not the same fact as a dated tenure, and a reprint cannot make it one");
    }

    [Fact]
    public async Task A_linked_chef_wins_over_the_legacy_source_note()
    {
        await using var db = TestHarness.NewContext("repartition-chef-note-superseded");
        var stage = db.SeedCatalog();

        var chef = db.SeedChef(Guid.NewGuid());
        chef.FirstName = "Pr.H.";
        chef.LastName  = "Harmouch";

        var service = db.SeedService(MedecineA, "Médecine A", chef);
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");

        var slot  = db.SeedSlot(stage, 1, 1, P1Start, P1End);
        var group = db.SeedGroup(1, 1);
        db.SeedSlotAssignment(1, db.SeedCohortFor(stage, group, 101), slot, service);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Single().ChefName.Should().Be("Pr.H. Harmouch",
            "the note is a fallback for services nobody has linked, not a competing record");
        report.Rows.Single().ChefIsFromSourceNote.Should().BeFalse();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("Un service de garde", null)]                        // a real description, no note
    [InlineData("Responsable (source) : ", null)]                    // prefix with nothing after it
    [InlineData("Responsable (source) : Pr.A.Settaf", "Pr.A.Settaf")]
    [InlineData("  Responsable (source) :  Pr.I.Nassar  ", "Pr.I.Nassar")]
    // Two chefs as the source wrote them: kept whole, because re-splitting on the hyphen would
    // also break the compound surnames that occur in the same column.
    [InlineData("Responsable (source) : Pr.Y.Tadlaoui- Pr.A.Elouartiti", "Pr.Y.Tadlaoui- Pr.A.Elouartiti")]
    public void The_source_note_is_read_only_when_it_is_actually_there(string description, string? expected) =>
        ServiceChefSourceNote.Read(description).Should().Be(expected);

    [Fact]
    public async Task A_period_with_no_service_for_a_row_leaves_the_cell_blank_and_is_counted()
    {
        // A hole is a planning gap worth reviewing before publication, so it is reported rather than
        // silently shortening the row.
        await using var db = TestHarness.NewContext("repartition-gap");
        var stage = db.SeedCatalog();

        var service = db.SeedService(MedecineA, "Médecine A");
        var other   = db.SeedService(Nephrologie, "Néphrologie");

        var p1 = db.SeedSlot(stage, 1, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 2, 2, P2Start, P2End);

        var groupOne = db.SeedGroup(1, 1);
        var cohort   = db.SeedCohortFor(stage, groupOne, 101);
        db.SeedSlotAssignment(1, cohort, p1, service);
        db.SeedSlotAssignment(2, cohort, p2, other);

        var groupTwo = db.SeedGroup(2, 2);
        db.SeedSlotAssignment(3, db.SeedCohortFor(stage, groupTwo, 102), p1, other);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Rows.Single(r => r.ServiceId == MedecineA).Cells[1].Should().BeNull();
        report.Summary.EmptyCells.Should().Be(1);
        report.Summary.PlannedCells.Should().Be(3);
    }

    [Fact]
    public async Task A_level_with_nothing_planned_yet_returns_an_empty_table()
    {
        await using var db = TestHarness.NewContext("repartition-empty");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Should().BeEmpty();
        result.Value.Columns.Should().BeEmpty();
        result.Value.Summary.Should().Be(new RepartitionSummary(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Slots_with_nothing_arranged_still_print_their_columns()
    {
        // The two empty tables are different states calling for opposite actions — author an axis, or
        // arrange into the one that exists. Building the axis from the cells collapsed them, so applying
        // a rotation cycle and then opening the répartition read exactly like an apply that had failed.
        await using var db = TestHarness.NewContext("repartition-slots-unarranged");
        var medecine = db.SeedCatalog();

        db.SeedSlot(medecine, 1, 1, P1Start, P1End);
        db.SeedSlot(medecine, 2, 2, P2Start, P2End);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Columns.Should().HaveCount(2);
        report.Columns[0].StartDate.Should().Be(P1Start);
        report.Rows.Should().BeEmpty();

        // What tells the two states apart: periods exist, nobody is in them.
        report.Summary.DeclaredSlotCount.Should().Be(2);
        report.Summary.RowCount.Should().Be(0);
    }

    [Fact]
    public async Task A_period_nobody_has_been_placed_in_keeps_its_column_beside_the_arranged_ones()
    {
        // A partially arranged level is the normal state mid-planning. The unarranged period is a hole
        // to review before publishing, not a column to hide — hiding it silently reshapes the printed
        // table and hides the hole with it.
        await using var db = TestHarness.NewContext("repartition-partial-axis");
        var medecine = db.SeedCatalog();
        var service  = db.SeedService(MedecineA, "Médecine A");

        var p1 = db.SeedSlot(medecine, 1, 1, P1Start, P1End);
        db.SeedSlot(medecine, 2, 2, P2Start, P2End);

        var group = db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedSlotAssignment(1, db.SeedCohortFor(medecine, group, 101), p1, service);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId), default)).Value;

        report.Columns.Should().HaveCount(2);
        report.Rows.Should().ContainSingle();
        report.Rows[0].Cells[0].Should().NotBeNull();
        report.Rows[0].Cells[1].Should().BeNull();
        report.Summary.EmptyCells.Should().Be(1);
        report.Summary.DeclaredSlotCount.Should().Be(2);
    }

    [Fact]
    public async Task An_unknown_level_is_refused()
    {
        await using var db = TestHarness.NewContext("repartition-no-level");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetLevelRepartitionQuery(404), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
    }

    [Fact]
    public async Task An_unknown_academic_year_is_refused_rather_than_widened()
    {
        await using var db = TestHarness.NewContext("repartition-no-year");
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetLevelRepartitionQuery(TestHarness.LevelId, AcademicYearId: 404), default);

        result.IsFailure.Should().BeTrue();
    }
}
