using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Schedule;
using PGSH.Application.Stages.Slots;
using PGSH.Application.Students.GetMany;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A stage keeps a cohort per (group, year) and a slot per (stage, year), so anything reached by
/// stage id alone spans every promotion that ever took it. On the imported data that meant 3,553
/// assignments for one 6ème année stage across six years where 688 were wanted, and it is what put
/// 3,500 rows in the evaluation-import canvas.
///
/// These cover the boundary itself: given two years of the same stage, each operation must see one.
/// </summary>
public class YearScopingTests
{
    private const int ServiceId = 1;
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record TwoYears(Stage Stage, Cohort Current, Cohort Previous);

    /// <summary>
    /// One stage run twice: a cohort of the current year and one of the year before, each with a
    /// student. The shape every screen on the Affectations page actually queries.
    /// </summary>
    private static async Task<TwoYears> SeedTwoYearsAsync(ApplicationDbContext db, bool closePeriods = false)
    {
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var service = db.SeedService(ServiceId, "Cardiologie");

        var current  = db.SeedCohort(stage, 10, "Groupe 10");
        var previous = db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);

        var thisYear = db.SeedAssignment(
            db.SeedRegistration("Sara", "Bennani", current.AcademicGroup), current);
        var lastYear = db.SeedAssignment(
            db.SeedRegistration("Ali", "Amrani", previous.AcademicGroup, TestHarness.PreviousYearId),
            previous);

        // started: false so the lifecycle test has something left to start; the import tests drive
        // the real Start() → CompletePeriod() path from here.
        db.SeedPeriod(thisYear, service, Start, End, started: false);
        db.SeedPeriod(lastYear, service, Start, End, started: false);

        if (closePeriods)
        {
            Close(thisYear);
            Close(lastYear);
        }

        await db.SaveChangesAsync();
        return new TwoYears(stage, current, previous);
    }

    // Driven through the real lifecycle: a period merely flagged complete leaves the assignment
    // Planned, so it would never reach a state the import can write to.
    private static void Close(InternshipAssignment assignment)
    {
        assignment.Start().IsSuccess.Should().BeTrue();
        foreach (var period in assignment.ServicePeriods.ToList())
            assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
    }

    // ── Import canvas ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_import_canvas_lists_only_the_requested_years_students()
    {
        await using var db = TestHarness.NewContext("canvas-one-year");
        await SeedTwoYearsAsync(db);

        var result = await TemplateHandler(db).Handle(
            new GetEvaluationImportTemplateQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric,
                TestHarness.CurrentYearId),
            default);

        result.IsSuccess.Should().BeTrue();
        var students = CapturingParser.Last!.Students;
        students.Should().ContainSingle();
        students[0].FullName.Should().Be("Sara Bennani");
    }

    [Fact]
    public async Task The_import_canvas_falls_back_to_the_current_year_when_none_is_given()
    {
        await using var db = TestHarness.NewContext("canvas-fallback");
        await SeedTwoYearsAsync(db);

        var result = await TemplateHandler(db).Handle(
            new GetEvaluationImportTemplateQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric),
            default);

        result.IsSuccess.Should().BeTrue();
        CapturingParser.Last!.Students.Should().ContainSingle()
            .Which.FullName.Should().Be("Sara Bennani", "2025-2026 is the year flagged current");
    }

    [Fact]
    public async Task The_import_canvas_names_the_year_it_was_built_for()
    {
        await using var db = TestHarness.NewContext("canvas-filename");
        await SeedTwoYearsAsync(db);

        var result = await TemplateHandler(db).Handle(
            new GetEvaluationImportTemplateQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric,
                TestHarness.PreviousYearId),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileName.Should().Contain("2024-2025",
            "two canvases downloaded minutes apart must not be the same file on disk");
    }

    [Fact]
    public async Task A_year_in_which_the_stage_had_no_students_is_refused_rather_than_returning_a_blank_sheet()
    {
        await using var db = TestHarness.NewContext("canvas-empty-year");
        await SeedTwoYearsAsync(db);
        db.SeedAcademicYear(3, "2020-2021", new DateOnly(2020, 9, 1), new DateOnly(2021, 8, 31));
        await db.SaveChangesAsync();

        var result = await TemplateHandler(db).Handle(
            new GetEvaluationImportTemplateQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric, 3),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceEvaluations.ImportYearHasNoStudents");
        result.Error.Description.Should().Contain("2020-2021");
    }

    [Fact]
    public async Task An_unknown_academic_year_is_refused()
    {
        await using var db = TestHarness.NewContext("canvas-unknown-year");
        await SeedTwoYearsAsync(db);

        var result = await TemplateHandler(db).Handle(
            new GetEvaluationImportTemplateQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric, 999),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.NotFound");
    }

    // ── Import apply ─────────────────────────────────────────────────────────

    [Fact]
    public async Task An_import_cannot_reach_a_student_of_another_year()
    {
        await using var db = TestHarness.NewContext("import-other-year");
        await SeedTwoYearsAsync(db, closePeriods: true);

        string lastYearsCne = await db.Registrations
            .Where(r => r.AcademicYearId == TestHarness.PreviousYearId)
            .Select(r => r.Student.CNE)
            .SingleAsync();

        var report = await PreviewHandler(db).Handle(
            new PreviewEvaluationImportQuery(
                TestHarness.StageId, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric,
                [new EvaluationImportRow(2, lastYearsCne, null, null, null, 14m, null)],
                TestHarness.CurrentYearId),
            default);

        report.IsSuccess.Should().BeTrue();
        report.Value.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(EvaluationImportRowStatus.UnknownStudent,
                "the row belongs to a promotion the selected year does not contain");
    }

    // ── Schedule grid ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_planning_grid_shows_only_the_requested_years_slots_and_cohorts()
    {
        await using var db = TestHarness.NewContext("grid-one-year");
        var seeded = await SeedTwoYearsAsync(db);

        db.SeedSlot(seeded.Stage, 100, 1, Start, End);
        db.SeedSlot(seeded.Stage, 200, 1, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31),
            TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var result = await new GetStageScheduleQueryHandler(
                db, new AcademicYearResolver(db), new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new GetStageScheduleQuery(TestHarness.StageId, TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Slots.Should().ContainSingle().Which.StartDate.Should().Be(Start);
        result.Value.Cohorts.Items.Should().ContainSingle().Which.CohortId.Should().Be(seeded.Current.Id);
        result.Value.Cohorts.TotalCount.Should().Be(1, "the count is the year's, not the page's");
        result.Value.Summary.TotalCohorts.Should().Be(1);
    }

    // ── Slot authoring ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_same_period_number_may_exist_once_per_year()
    {
        await using var db = TestHarness.NewContext("slot-per-year");
        var seeded = await SeedTwoYearsAsync(db);
        db.SeedSlot(seeded.Stage, 100, 1, Start, End);
        await db.SaveChangesAsync();

        var result = await new CreateStageSlotCommandHandler(db, new SlotOverlapGuard(db)).Handle(
            new CreateStageSlotCommand(
                TestHarness.StageId, TestHarness.PreviousYearId, 1, null,
                new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31)),
            default);

        result.IsSuccess.Should().BeTrue("P1 of 2024-2025 is a different period from P1 of 2025-2026");
    }

    [Fact]
    public async Task A_duplicate_period_number_within_one_year_is_still_refused()
    {
        await using var db = TestHarness.NewContext("slot-dup-same-year");
        var seeded = await SeedTwoYearsAsync(db);
        db.SeedSlot(seeded.Stage, 100, 1, Start, End);
        await db.SaveChangesAsync();

        var result = await new CreateStageSlotCommandHandler(db, new SlotOverlapGuard(db)).Handle(
            new CreateStageSlotCommand(
                TestHarness.StageId, TestHarness.CurrentYearId, 1, null,
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.DuplicatePeriodNumber");
    }

    [Fact]
    public async Task Identical_calendar_windows_in_different_years_do_not_overlap()
    {
        await using var db = TestHarness.NewContext("slot-overlap-cross-year");
        var seeded = await SeedTwoYearsAsync(db);
        db.SeedSlot(seeded.Stage, 100, 1, Start, End);
        await db.SaveChangesAsync();

        // Same dates, previous year: two promotions never share a student, so this is not a clash.
        var result = await new CreateStageSlotCommandHandler(db, new SlotOverlapGuard(db)).Handle(
            new CreateStageSlotCommand(
                TestHarness.StageId, TestHarness.PreviousYearId, 2, null, Start, End),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Overlapping_windows_within_one_year_are_still_refused()
    {
        await using var db = TestHarness.NewContext("slot-overlap-same-year");
        var seeded = await SeedTwoYearsAsync(db);
        db.SeedSlot(seeded.Stage, 100, 1, Start, End);
        await db.SaveChangesAsync();

        var result = await new CreateStageSlotCommandHandler(db, new SlotOverlapGuard(db)).Handle(
            new CreateStageSlotCommand(
                TestHarness.StageId, TestHarness.CurrentYearId, 2, null,
                new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotOverlap");
    }

    [Fact]
    public async Task A_slot_in_an_unknown_year_is_refused()
    {
        await using var db = TestHarness.NewContext("slot-unknown-year");
        await SeedTwoYearsAsync(db);

        var result = await new CreateStageSlotCommandHandler(db, new SlotOverlapGuard(db)).Handle(
            new CreateStageSlotCommand(TestHarness.StageId, 999, 1, null, Start, End), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.NotFound");
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Starting_a_stage_leaves_other_years_assignments_untouched()
    {
        await using var db = TestHarness.NewContext("start-one-year");
        await SeedTwoYearsAsync(db);

        var started = await new StagePeriodRunner(db).StartStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        started.IsSuccess.Should().BeTrue();
        started.Value.Should().Be(1, "only this year's single rotation was in scope");

        var byYear = await db.InternshipAssignments
            .Include(a => a.Cohort).ThenInclude(c => c.AcademicGroup)
            .Include(a => a.ServicePeriods)
            .ToDictionaryAsync(a => a.Cohort.AcademicGroup.AcademicYearId,
                               a => a.ServicePeriods.All(p => p.IsStarted));

        byYear[TestHarness.CurrentYearId].Should().BeTrue();
        byYear[TestHarness.PreviousYearId].Should().BeFalse();
    }

    [Fact]
    public async Task Affecting_students_by_stage_is_confined_to_one_year()
    {
        await using var db = TestHarness.NewContext("affect-one-year");
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var current = db.SeedCohort(stage, 10, "Groupe 10");
        var previous = db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);

        // Registrations with no assignment yet — exactly what the affectation step looks for.
        db.SeedRegistration("Sara", "Bennani", current.AcademicGroup);
        db.SeedRegistration("Ali", "Amrani", previous.AcademicGroup, TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var result = await new StudentAffectationService(db)
            .AssignByStageAsync(TestHarness.StageId, TestHarness.CurrentYearId, null, default);

        result.SuccessCount.Should().Be(1);

        var assignment = await db.InternshipAssignments.SingleAsync();
        assignment.CurrentCohortId.Should().Be(current.Id,
            "last year's registration must not be affected by a run scoped to this year");
        assignment.CurrentCohortId.Should().NotBe(previous.Id);
    }

    // ── The resolver itself ──────────────────────────────────────────────────

    [Fact]
    public async Task With_no_year_flagged_current_the_resolver_refuses_rather_than_widening()
    {
        // The whole rule in one case: absence resolves to the current year, and if there isn't one it
        // is an error. Falling through to "all years" is the defect this class exists to prevent.
        await using var db = TestHarness.NewContext("resolver-no-current");
        db.SeedAcademicYear(7, "2019-2020", new DateOnly(2019, 9, 1), new DateOnly(2020, 8, 31));
        await db.SaveChangesAsync();

        var resolved = await new AcademicYearResolver(db).ResolveAsync(null, default);

        resolved.IsFailure.Should().BeTrue();
        resolved.Error.Code.Should().Be("AcademicYears.NoCurrent");
    }

    // ── Student list ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_year_narrows_the_student_population_not_just_its_columns()
    {
        await using var db = TestHarness.NewContext("students-one-year");
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedRegistration("Sara", "Bennani");
        db.SeedRegistration("Ali", "Amrani", null, TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null, AcademicYearId: TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1,
            "a student with no registration that year is not a student of that year");
        result.Value.Items.Should().ContainSingle().Which.LastName.Should().Be("Bennani");
    }

    [Fact]
    public async Task Omitting_the_year_still_returns_every_student()
    {
        await using var db = TestHarness.NewContext("students-all-years");
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedRegistration("Sara", "Bennani");
        db.SeedRegistration("Ali", "Amrani", null, TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2, "the global search must still reach every promotion");
    }

    // ── Student list, filtered by promotion ──────────────────────────────────

    private const int OtherLevelId = 7;

    /// <summary>
    /// The whole point of the filter, and the one way it can quietly be wrong.
    /// </summary>
    /// <remarks>
    /// Asked as two independent conditions — « inscrit cette année » and « a été en 3ᵉ année » — a
    /// student who <em>was</em> in the 3ᵉ année years ago and sits in the 7ᵉ today satisfies both, on
    /// two different registrations. On the real base 2 635 students have repeated at least once, so
    /// this is the ordinary case rather than an edge one. The pair must hold on a single row.
    /// </remarks>
    [Fact]
    public async Task A_level_and_a_year_must_be_satisfied_by_the_same_registration()
    {
        await using var db = TestHarness.NewContext("students-level-same-registration");
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedLevel(OtherLevelId, "7ème année", 7);

        // In the 3ème année last year, in the 7ème this year: matches each condition once, and the
        // conjunction never.
        var repeater = db.SeedRegistration("Yassine", "Idrissi", null, TestHarness.PreviousYearId);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = TestHarness.CurrentYearId, LevelId = OtherLevelId,
            StudentId = repeater.StudentId,
        });

        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null,
                LevelId: TestHarness.LevelId, AcademicYearId: TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(
            "only the student whose *this year's* registration is at that level belongs to the promotion")
            .Which.LastName.Should().Be("Bennani");
    }

    [Fact]
    public async Task Filtering_by_level_keeps_only_that_promotion()
    {
        await using var db = TestHarness.NewContext("students-level-filter");
        db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "7ème année", 7);

        db.SeedRegistration("Sara", "Bennani");
        db.SeedRegistration("Ali", "Amrani");
        db.SeedRegistration("Nadia", "Fassi", null, TestHarness.CurrentYearId, OtherLevelId);
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null,
                LevelId: OtherLevelId, AcademicYearId: TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle().Which.LastName.Should().Be("Fassi");
    }

    /// <summary>The control: without it, a filter that returns nothing for every level would pass
    /// the two tests above.</summary>
    [Fact]
    public async Task Omitting_the_level_keeps_the_whole_promotion()
    {
        await using var db = TestHarness.NewContext("students-no-level-filter");
        db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "7ème année", 7);

        db.SeedRegistration("Sara", "Bennani");
        db.SeedRegistration("Ali", "Amrani");
        db.SeedRegistration("Nadia", "Fassi", null, TestHarness.CurrentYearId, OtherLevelId);
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null,
                AcademicYearId: TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
    }

    /// <summary>
    /// A level with no year is the cross-year read, and it is deliberately left reachable: the same
    /// thing an omitted year already means for the list as a whole.
    /// </summary>
    [Fact]
    public async Task A_level_without_a_year_reaches_every_year_of_that_level()
    {
        await using var db = TestHarness.NewContext("students-level-all-years");
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedRegistration("Sara", "Bennani");
        db.SeedRegistration("Ali", "Amrani", null, TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null, LevelId: TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task An_unknown_level_returns_nobody_rather_than_everybody()
    {
        await using var db = TestHarness.NewContext("students-unknown-level");
        db.SeedCatalog();
        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null,
                LevelId: 4242, AcademicYearId: TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GetEvaluationImportTemplateQueryHandler TemplateHandler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new CapturingParser());

    private static PreviewEvaluationImportQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(new EvaluationImportPlanner(db, new AcademicYearResolver(db), db.AdminAuthorizer()));

    /// <summary>
    /// Stands in for the ClosedXML adapter and keeps the last template it was handed, so a test can
    /// assert on the rows that went into the sheet without opening a workbook.
    /// </summary>
    private sealed class CapturingParser : IEvaluationSheetParser
    {
        [ThreadStatic] private static EvaluationImportTemplate? _last;

        public static EvaluationImportTemplate? Last => _last;

        public IReadOnlyList<EvaluationImportRow> Parse(Stream sheet) => [];

        public byte[] BuildTemplate(EvaluationImportTemplate template)
        {
            _last = template;
            return [1];
        }
    }
}
