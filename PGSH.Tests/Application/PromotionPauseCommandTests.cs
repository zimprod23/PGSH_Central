using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Application.Calendar.Pauses;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Declaring, previewing, correcting and revoking a promotion's exam window.
///
/// <para>⚠ <b>The act writes one row and moves no date, deliberately</b> — see
/// <c>PromotionPause</c>. What these tests pin is therefore the two halves that matter: that the window
/// reaches the promotion's calendar and <em>only</em> that promotion's, and that what it costs the plan
/// already laid is counted honestly, before any write.</para>
/// </summary>
public class PromotionPauseCommandTests
{
    private const int OtherLevelId = 7;

    private static readonly DateOnly ExamStart = new(2026, 1, 12);
    private static readonly DateOnly ExamEnd = new(2026, 1, 16);

    private static IDateTimeProvider Clock(DateTime? now = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now ?? new DateTime(2025, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static PromotionPauseContext Context(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static PromotionPauseImpactReader Reader(ApplicationDbContext db) =>
        new(db, new WorkingDayProvider(db));

    private static DeclarePromotionPauseCommandHandler Declarer(
        ApplicationDbContext db, DateTime? now = null) =>
        new(db, Context(db), new PromotionPauseCalendarGuard(db), Reader(db), Clock(now));

    private static DeclarePromotionPauseCommand Declare(
        DateOnly? start = null, DateOnly? end = null, int levelId = TestHarness.LevelId,
        string reason = "Examens du 1er semestre") =>
        new(levelId, start ?? ExamStart, end ?? ExamEnd, PauseKind.Exam, reason);

    /// <summary>
    /// The catalogue, a second promotion sharing the year, and one créneau of the first promotion laid
    /// straight across the exam week.
    /// </summary>
    private static ApplicationDbContext Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();

        db.SeedLevel(OtherLevelId, "4ème année", year: 4);
        db.SeedStage(2, "Pédiatrie", levelId: OtherLevelId);

        db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6));

        return db;
    }

    [Fact]
    public async Task Declaring_records_the_window_and_reports_what_it_costs()
    {
        await using var db = Seed(nameof(Declaring_records_the_window_and_reports_what_it_costs));
        await db.SaveChangesAsync();

        var result = await Declarer(db).Handle(Declare(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkingDaysLost.Should().Be(5, "Monday 12 to Friday 16 January 2026");
        result.Value.SlotsSpanning.Should().Be(1);

        var stored = await db.PromotionPauses.SingleAsync();
        stored.LevelId.Should().Be(TestHarness.LevelId);
        stored.AcademicYearId.Should().Be(TestHarness.CurrentYearId, "an omitted year is the current one");
    }

    /// <summary>
    /// The reason the whole phase exists: two promotions rotate through the same services on the same
    /// morning, and only one of them is sitting an exam.
    /// </summary>
    [Fact]
    public async Task A_window_reaches_its_own_promotions_calendar_and_no_other()
    {
        await using var db = Seed(nameof(A_window_reaches_its_own_promotions_calendar_and_no_other));
        await db.SaveChangesAsync();

        (await Declarer(db).Handle(Declare(), default)).IsSuccess.Should().BeTrue();

        var provider = new WorkingDayProvider(db);
        var mine = await provider.ForPromotionAsync(TestHarness.CurrentYearId, TestHarness.LevelId, default);
        var theirs = await provider.ForPromotionAsync(TestHarness.CurrentYearId, OtherLevelId, default);
        var faculty = await provider.BuildAsync(default);

        mine.Count(ExamStart, ExamEnd).Should().Be(0);
        theirs.Count(ExamStart, ExamEnd).Should().Be(5);
        faculty.Count(ExamStart, ExamEnd).Should().Be(5);
    }

    /// <summary>
    /// The axis laid afterwards steps over the window on its own — that is the compensation, and it is
    /// in jours ouvrables rather than in calendar days pushed onto each assignment.
    /// </summary>
    [Fact]
    public async Task An_axis_laid_afterwards_steps_over_the_window()
    {
        await using var db = Seed(nameof(An_axis_laid_afterwards_steps_over_the_window));
        await db.SaveChangesAsync();

        var axis = new GenerateAxisWindowsQueryHandler(
            db, new AcademicYearResolver(db), new WorkingDayProvider(db));

        var before = await axis.Handle(
            new GenerateAxisWindowsQuery(1, new DateOnly(2026, 1, 5), AxisColumnUnit.WorkingDays, 15,
                LevelId: TestHarness.LevelId), default);

        (await Declarer(db).Handle(Declare(), default)).IsSuccess.Should().BeTrue();

        var after = await axis.Handle(
            new GenerateAxisWindowsQuery(1, new DateOnly(2026, 1, 5), AxisColumnUnit.WorkingDays, 15,
                LevelId: TestHarness.LevelId), default);

        after.Value.Columns[0].EndDate.Should().Be(before.Value.Columns[0].EndDate.AddDays(7));
        after.Value.Columns[0].WorkingDays.Should().Be(15, "the column still holds its fifteen days");
        after.Value.Columns[0].Pauses.Should().ContainSingle().Which.Should().Be("Examens du 1er semestre");
        after.Value.Columns[0].Holidays.Should().BeEmpty("a pause is not a jour férié");
    }

    /// <summary>
    /// The same request twice gives the same dates. The pause retired on 18/09/2026 accumulated; this is what
    /// replaces it for a promotion, and it is why the window can be corrected at all.
    /// </summary>
    [Fact]
    public async Task Redeclaring_the_same_window_is_refused_rather_than_applied_twice()
    {
        await using var db = Seed(nameof(Redeclaring_the_same_window_is_refused_rather_than_applied_twice));
        await db.SaveChangesAsync();

        (await Declarer(db).Handle(Declare(), default)).IsSuccess.Should().BeTrue();
        var second = await Declarer(db).Handle(Declare(), default);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("PromotionPauses.Overlap");
        (await db.PromotionPauses.CountAsync()).Should().Be(1, "a refused act writes nothing");
    }

    [Fact]
    public async Task Two_windows_of_one_promotion_may_not_overlap()
    {
        await using var db = Seed(nameof(Two_windows_of_one_promotion_may_not_overlap));
        await db.SaveChangesAsync();

        (await Declarer(db).Handle(Declare(), default)).IsSuccess.Should().BeTrue();

        // Touching by one day is an overlap: the ends are inclusive.
        var touching = await Declarer(db).Handle(
            Declare(ExamEnd, ExamEnd.AddDays(3), reason: "Rattrapage"), default);

        touching.IsFailure.Should().BeTrue();
        touching.Error.Code.Should().Be("PromotionPauses.Overlap");

        var adjacent = await Declarer(db).Handle(
            Declare(ExamEnd.AddDays(1), ExamEnd.AddDays(3), reason: "Rattrapage"), default);

        adjacent.IsSuccess.Should().BeTrue("the day after is a different day");
    }

    /// <summary>The control: another promotion's window on the same dates is not a clash at all.</summary>
    [Fact]
    public async Task Another_promotion_may_declare_the_same_dates()
    {
        await using var db = Seed(nameof(Another_promotion_may_declare_the_same_dates));
        await db.SaveChangesAsync();

        (await Declarer(db).Handle(Declare(), default)).IsSuccess.Should().BeTrue();

        var other = await Declarer(db).Handle(Declare(levelId: OtherLevelId), default);

        other.IsSuccess.Should().BeTrue();
        (await db.PromotionPauses.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_reports_what_the_declaration_will()
    {
        await using var db = Seed(nameof(The_preview_writes_nothing_and_reports_what_the_declaration_will));
        await db.SaveChangesAsync();

        var preview = await new PreviewPromotionPauseQueryHandler(Context(db), Reader(db)).Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.WorkingDaysLost.Should().Be(5);
        preview.Value.SlotsSpanning.Should().Be(1);
        preview.Value.Slots.Should().ContainSingle()
            .Which.WorkingDaysBefore.Should().BeGreaterThan(preview.Value.Slots[0].WorkingDaysAfter);

        (await db.PromotionPauses.AnyAsync()).Should().BeFalse();

        var declared = await Declarer(db).Handle(Declare(reason: "Examens"), default);
        declared.Value.WorkingDaysLost.Should().Be(preview.Value.WorkingDaysLost);
        declared.Value.SlotsSpanning.Should().Be(preview.Value.SlotsSpanning);
    }

    /// <summary>
    /// ⚠ The trap the reader exists to avoid: measured against a calendar that already holds the window,
    /// every window ever declared costs zero. Previewing a <em>correction</em> must leave the recorded
    /// one out.
    /// </summary>
    [Fact]
    public async Task Previewing_a_correction_measures_the_new_dates_and_not_the_old_ones()
    {
        await using var db = Seed(nameof(Previewing_a_correction_measures_the_new_dates_and_not_the_old_ones));
        var pause = db.SeedPromotionPause(ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));

        var blind = await handler.Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        var honest = await handler.Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens",
                ExcludingPauseId: pause.Id), default);

        blind.Value.WorkingDaysLost.Should().Be(0, "the window is already in the calendar it is measured on");
        honest.Value.WorkingDaysLost.Should().Be(5);
    }

    /// <summary>
    /// « Rien n'est encore planifié » and « rien n'est touché » are the same zero and opposite
    /// situations, so the report says which one it found.
    /// </summary>
    [Fact]
    public async Task A_promotion_with_no_grid_is_told_so_rather_than_shown_a_bare_zero()
    {
        await using var db = Seed(nameof(A_promotion_with_no_grid_is_told_so_rather_than_shown_a_bare_zero));
        await db.SaveChangesAsync();

        var preview = await new PreviewPromotionPauseQueryHandler(Context(db), Reader(db)).Handle(
            new PreviewPromotionPauseQuery(OtherLevelId, ExamStart, ExamEnd, "Examens"), default);

        preview.Value.SlotsSpanning.Should().Be(0);
        preview.Value.Warnings.Should().Contain(w => w.Contains("Aucun créneau"));
    }

    /// <summary>
    /// ⚠ <b>A report must not prescribe a button that refuses.</b> Found on the live base 2026-09-06:
    /// the report said « reposez l'axe de la promotion », and for the 3ᵉ MED — 804 published cells —
    /// <c>ApplyRotationCycleCommand</c> refuses outright. So the remedy depends on whether anything has
    /// been published from the grid, and the report says which of the two situations it is in.
    /// </summary>
    [Fact]
    public async Task A_published_grid_is_told_the_axis_cannot_be_relaid_rather_than_told_to_relay_it()
    {
        await using var db = Seed(nameof(A_published_grid_is_told_the_axis_cannot_be_relaid_rather_than_told_to_relay_it));

        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var service = db.SeedService(1, "Cardiologie A");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var slot = db.StageSlots.Local.First();
        var cell = db.SeedSlotAssignment(1, cohort, slot, service);
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));
        var query = new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens");

        // Nothing published yet: re-laying is a real act, so the report names it.
        var unpublished = await handler.Handle(query, default);

        unpublished.Value.PublishedCellsInGrid.Should().Be(0);
        unpublished.Value.Warnings.Should().Contain(w => w.Contains("Reposez l'axe"));

        // Publish one cell — one is enough, because the apply guard is promotion-wide.
        var registration = db.SeedRegistration("Houda", "Aamoud", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(assignment, service, slot.StartDate, slot.EndDate);
        db.SeedCoverage(period, cell);
        await db.SaveChangesAsync();

        var published = await handler.Handle(query, default);

        published.Value.PublishedCellsInGrid.Should().Be(1);
        published.Value.Warnings.Should().NotContain(w => w.Contains("Reposez l'axe"),
            "that button refuses once anything is published");
        published.Value.Warnings.Should().Contain(w => w.Contains("déjà publié"));

        // ⚠ And it must name the remedy that DOES exist. The sentence « déplacer une colonne déjà
        // publiée n'est pas encore possible » was true the day it was written and outlived itself when
        // phase 17.1 shipped the move; a report naming no remedy reads as « rien à faire ».
        published.Value.Warnings.Should().NotContain(w => w.Contains("n'est pas encore possible"));
        published.Value.Warnings.Should().Contain(w => w.Contains("déplaçant les colonnes"));
    }

    /// <summary>
    /// ⚠ <b>« Reposer est refusé » is true and, on its own, useless.</b> What repairs a published
    /// promotion is moving the crossed columns one at a time, and whether that is worth starting
    /// depends on how many of them the act would accept — so the report counts them, against the same
    /// rule <c>InternshipAssignment.Reschedule</c> refuses on.
    ///
    /// <para>The three cases say different things — « tout est réparable », « une partie l'est »,
    /// « ces jours sont perdus » — and a bare number would leave the reader to guess which.</para>
    /// </summary>
    [Fact]
    public async Task A_published_grid_is_told_how_many_of_its_columns_the_move_would_accept()
    {
        await using var db = Seed(nameof(A_published_grid_is_told_how_many_of_its_columns_the_move_would_accept));

        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var service = db.SeedService(1, "Cardiologie A");
        var slot = db.StageSlots.Local.First();

        // A second column across the same window, so « some » is distinguishable from « all ».
        var secondSlot = db.SeedSlot(stage, slotId: 2, periodNumber: 2,
            new DateOnly(2026, 1, 7), new DateOnly(2026, 2, 8));

        var untouched = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var running = db.SeedCohort(stage, groupId: 2, groupLabel: "G2");
        var cellA = db.SeedSlotAssignment(1, untouched, slot, service);
        var cellB = db.SeedSlotAssignment(2, running, secondSlot, service);
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));
        var query = new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens");

        // Publish both columns, neither rotation started: the move refuses nothing.
        var first = db.SeedRegistration("Houda", "Aamoud", untouched.AcademicGroup);
        var plannedPeriod = db.SeedPeriod(
            db.SeedAssignment(first, untouched), service, slot.StartDate, slot.EndDate, started: false);
        db.SeedCoverage(plannedPeriod, cellA);

        var second = db.SeedRegistration("Yassine", "Bennani", running.AcademicGroup);
        var startedPeriod = db.SeedPeriod(
            db.SeedAssignment(second, running), service,
            secondSlot.StartDate, secondSlot.EndDate, started: false);
        db.SeedCoverage(startedPeriod, cellB);
        await db.SaveChangesAsync();

        var allMovable = await handler.Handle(query, default);

        allMovable.Value.SlotsSpanning.Should().Be(2);
        allMovable.Value.SlotsMovable.Should().Be(2);
        allMovable.Value.Warnings.Should().Contain(w => w.Contains("sont tous déplaçables"));

        // Start the second one. Its column is now refused; the first is untouched and still movable.
        startedPeriod.IsStarted = true;
        await db.SaveChangesAsync();

        var partly = await handler.Handle(query, default);

        partly.Value.SlotsMovable.Should().Be(1);
        partly.Value.Warnings.Should().Contain(w => w.Contains("1 des 2 créneau(x)"));

        // ⚠ A journée de présence blocks exactly as a start does, and it is the silent one: the period
        // below is never started, never marked, and still must not move.
        db.SeedAttendance(plannedPeriod);
        await db.SaveChangesAsync();

        var none = await handler.Handle(query, default);

        none.Value.SlotsMovable.Should().Be(0);
        none.Value.Warnings.Should().Contain(w => w.Contains("Aucun des 2 créneau(x)"));
        none.Value.Warnings.Should().Contain(w => w.Contains("perdus"));
    }

    /// <summary>
    /// ⚠ <b>Rotations written outside the grid had no branch at all, and that is two opposite silences
    /// in one.</b> A période from the canevas des affectations, a délocalisation or a legacy import
    /// carries no cell, so neither remedy reaches it: re-laying starts from the axis and the move
    /// starts from the cells. Before this the case fell either into « Reposez l'axe » — a gesture that
    /// succeeds and changes nothing for them — or, with nothing under way, into no warning whatsoever.
    /// </summary>
    [Fact]
    public async Task Rotations_with_no_creneau_are_named_rather_than_sent_to_relay_the_axis()
    {
        await using var db = Seed(nameof(Rotations_with_no_creneau_are_named_rather_than_sent_to_relay_the_axis));

        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var service = db.SeedService(1, "Cardiologie A");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        // The promotion's only column moved off the window, so nothing of the grid crosses it.
        var slot = db.StageSlots.Local.First();
        slot.StartDate = new DateOnly(2026, 3, 2);
        slot.EndDate = new DateOnly(2026, 4, 3);

        // …and an off-grid rotation laid straight across it: no cell, no coverage.
        var registration = db.SeedRegistration("Salma", "Idrissi", cohort.AcademicGroup);
        db.SeedPeriod(
            db.SeedAssignment(registration, cohort), service,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6), started: false);
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));
        var preview = await handler.Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        preview.Value.SlotsSpanning.Should().Be(0);
        preview.Value.PeriodsSpanning.Should().Be(1);

        preview.Value.Warnings.Should().Contain(w => w.Contains("hors grille"));
        preview.Value.Warnings.Should().Contain(w => w.Contains("canevas des affectations"));

        // The control: neither of the two grid remedies may be prescribed here, because neither
        // reaches a période with no cell behind it.
        preview.Value.Warnings.Should().NotContain(w => w.Contains("Reposez l'axe"));
        preview.Value.Warnings.Should().NotContain(w => w.Contains("déplaçant les colonnes"));

        // ⚠ And it is not the « rien n'est planifié » sentence either: that zero means the opposite —
        // declaring the window in advance, with an axis still to lay.
        preview.Value.Warnings.Should().NotContain(w => w.Contains("Aucun créneau ni aucune rotation"));
    }

    /// <summary>
    /// ⚠ <b>The « aucun jour férié » caption is about the academic YEAR, never about the window</b> — a
    /// correction made after driving the real screen on 2026-09-06. An exam week holds no jour férié in
    /// the ordinary case, so measured on its own five days the flag fired on nearly every window and
    /// said nothing. A caption that fires whatever the data says is noise, and noise is dismissed.
    /// </summary>
    [Fact]
    public async Task The_empty_calendar_caption_is_about_the_year_and_not_about_the_window()
    {
        await using var db = Seed(nameof(The_empty_calendar_caption_is_about_the_year_and_not_about_the_window));

        // A holiday the year holds, nowhere near the exam week.
        db.SeedHoliday(new DateOnly(2025, 11, 18), "Fête de l'Indépendance");
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));

        var withCalendar = await handler.Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        withCalendar.Value.CalendarIsEmpty.Should().BeFalse(
            "the year has a holiday on file, even though this particular week does not");
        withCalendar.Value.Warnings.Should().NotContain(w => w.Contains("Aucun jour férié"));

        // The control: a year nobody has entered a calendar for still says so.
        await using var bare = Seed(nameof(The_empty_calendar_caption_is_about_the_year_and_not_about_the_window) + "-bare");
        await bare.SaveChangesAsync();

        var withoutCalendar = await new PreviewPromotionPauseQueryHandler(Context(bare), Reader(bare))
            .Handle(new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        withoutCalendar.Value.CalendarIsEmpty.Should().BeTrue();
        withoutCalendar.Value.Warnings.Should().Contain(w => w.Contains("Aucun jour férié"));
    }

    [Fact]
    public async Task Correcting_reports_the_union_of_the_span_it_leaves_and_the_one_it_reaches()
    {
        await using var db = Seed(nameof(Correcting_reports_the_union_of_the_span_it_leaves_and_the_one_it_reaches));
        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);

        // A second créneau, well after the window: it must be counted only once it is reached.
        db.SeedSlot(stage, slotId: 2, periodNumber: 2,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 3));

        var pause = db.SeedPromotionPause(ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var handler = new CorrectPromotionPauseCommandHandler(
            db, new PromotionPauseCalendarGuard(db), Reader(db));

        var result = await handler.Handle(
            new CorrectPromotionPauseCommand(
                pause.Id, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13), PauseKind.Exam,
                "Examens du 1er semestre", true),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DatesMoved.Should().BeTrue();
        result.Value.SlotsSpanning.Should().Be(2, "the one it leaves and the one it arrives at");
        (await db.PromotionPauses.SingleAsync()).StartDate.Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public async Task Confirming_a_window_that_did_not_move_reports_no_creneau()
    {
        await using var db = Seed(nameof(Confirming_a_window_that_did_not_move_reports_no_creneau));
        var pause = db.SeedPromotionPause(ExamStart, ExamEnd, confirmed: false);
        await db.SaveChangesAsync();

        var result = await new CorrectPromotionPauseCommandHandler(
                db, new PromotionPauseCalendarGuard(db), Reader(db))
            .Handle(
                new CorrectPromotionPauseCommand(
                    pause.Id, ExamStart, ExamEnd, PauseKind.Exam, "Examens", IsConfirmed: true),
                default);

        result.Value.DatesMoved.Should().BeFalse();
        result.Value.SlotsSpanning.Should().Be(0);
        (await db.PromotionPauses.SingleAsync()).IsConfirmed.Should().BeTrue();
    }

    /// <summary>
    /// ⚠ Prospective, and stated: revoking a window already begun walks nothing back, because declaring
    /// pushed nothing. What comes back are the days, in every measurement from now on.
    /// </summary>
    [Fact]
    public async Task Revoking_gives_the_days_back_and_says_whether_the_window_had_begun()
    {
        await using var db = Seed(nameof(Revoking_gives_the_days_back_and_says_whether_the_window_had_begun));
        var pause = db.SeedPromotionPause(ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var result = await new RevokePromotionPauseCommandHandler(
                db, Reader(db), Clock(new DateTime(2026, 1, 14, 9, 0, 0, DateTimeKind.Utc)))
            .Handle(new RevokePromotionPauseCommand(pause.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.HadBegun.Should().BeTrue();
        result.Value.SlotsSpanning.Should().Be(1, "the créneau laid across it keeps the dates it was given");

        (await db.PromotionPauses.AnyAsync()).Should().BeFalse();
        (await new WorkingDayProvider(db)
            .ForPromotionAsync(TestHarness.CurrentYearId, TestHarness.LevelId, default))
            .Count(ExamStart, ExamEnd).Should().Be(5);
    }

    [Fact]
    public async Task Revoking_a_window_that_is_not_there_refuses()
    {
        await using var db = Seed(nameof(Revoking_a_window_that_is_not_there_refuses));
        await db.SaveChangesAsync();

        var result = await new RevokePromotionPauseCommandHandler(db, Reader(db), Clock())
            .Handle(new RevokePromotionPauseCommand(4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PromotionPauses.NotFound");
    }

    [Fact]
    public async Task The_withdrawal_marker_is_refused_on_the_preview_as_on_the_act()
    {
        await using var db = Seed(nameof(The_withdrawal_marker_is_refused_on_the_preview_as_on_the_act));
        db.SeedLevel(98, "Retrait", year: 0);
        await db.SaveChangesAsync();

        var preview = await new PreviewPromotionPauseQueryHandler(Context(db), Reader(db)).Handle(
            new PreviewPromotionPauseQuery(98, ExamStart, ExamEnd, "Examens"), default);

        var declared = await Declarer(db).Handle(Declare(levelId: 98), default);

        preview.Error.Code.Should().Be("Levels.NotAPromotion");
        declared.Error.Code.Should().Be("Levels.NotAPromotion");
        (await db.PromotionPauses.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_list_is_scoped_to_a_year_and_reports_what_each_window_costs()
    {
        await using var db = Seed(nameof(The_list_is_scoped_to_a_year_and_reports_what_each_window_costs));
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedPromotionPause(ExamStart, ExamEnd);
        db.SeedPromotionPause(new DateOnly(2025, 1, 13), new DateOnly(2025, 1, 17),
            "Examens 2024-2025", academicYearId: TestHarness.PreviousYearId);

        // A window over a weekend: real, and it costs no day of stage.
        db.SeedPromotionPause(new DateOnly(2026, 2, 7), new DateOnly(2026, 2, 8), "Week-end",
            levelId: OtherLevelId);

        await db.SaveChangesAsync();

        var page = await new GetPromotionPausesQueryHandler(
                db, new AcademicYearResolver(db), new WorkingDayProvider(db))
            .Handle(new GetPromotionPausesQuery(), default);

        page.Value.TotalCount.Should().Be(2, "an omitted year is the current one, never all of them");
        page.Value.Items.Single(p => p.LevelId == TestHarness.LevelId).WorkingDaysLost.Should().Be(5);
        page.Value.Items.Single(p => p.LevelId == OtherLevelId).WorkingDaysLost.Should().Be(0);
    }

    /// <summary>
    /// ⚠ <b>Une colonne vidée n'est pas une colonne raccourcie, et un intervalle « 0 – 10 » ne le disait
    /// pas.</b> Trouvé le 17/09/2026 en pilotant l'écran réel : une fenêtre sur décembre 2026 — 23 jours
    /// ouvrables contre des colonnes de 15 — vide une colonne de chacun des 8 stages de la 3ᵉ MED, et la
    /// seule trace était le bord gauche d'un intervalle dans une cellule de tableau. Le remède diffère :
    /// une colonne qui garde 12 jours sur 15 se rattrape d'un décalage, une colonne qui en garde zéro est
    /// une rotation pendant laquelle personne ne sert rien.
    /// </summary>
    [Fact]
    public async Task A_column_the_window_empties_is_named_apart_from_one_it_merely_shortens()
    {
        await using var db = Seed(nameof(A_column_the_window_empties_is_named_apart_from_one_it_merely_shortens));

        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var service = db.SeedService(1, "Cardiologie A");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        // Le créneau du fixture (05/01 → 06/02) traverse la fenêtre et en ressort raccourci. Celui-ci
        // tient tout entier dedans : il n'en ressort pas du tout.
        var swallowed = db.SeedSlot(stage, slotId: 2, periodNumber: 2, ExamStart, ExamEnd);
        db.SeedSlotAssignment(2, cohort, swallowed, service);
        await db.SaveChangesAsync();

        var handler = new PreviewPromotionPauseQueryHandler(Context(db), Reader(db));

        var preview = await handler.Handle(
            new PreviewPromotionPauseQuery(TestHarness.LevelId, ExamStart, ExamEnd, "Examens"), default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.SlotsSpanning.Should().Be(2);

        // Un seul des deux est vidé — l'autre garde des jours, et les confondre est tout le défaut.
        preview.Value.SlotsEmptied.Should().Be(1);
        preview.Value.CellsInEmptiedSlots.Should().Be(1, "la cellule reste en place dans la colonne vide");
        preview.Value.Warnings.Should().Contain(w => w.Contains("perdent la totalité"));
        preview.Value.Warnings.Should().Contain(w => w.Contains("1 cellule(s)"));

        // ⚠ Et il s'ajoute au remède au lieu de le remplacer : une colonne vidée arrive que l'axe soit
        // publié, en cours ou au repos, donc ce n'est pas une variante de ces cas mais un fait de plus.
        preview.Value.Warnings.Should().Contain(w => w.Contains("Reposez l'axe"));

        // Le contrôle, et c'est lui qui empêche la phrase de devenir du bruit : une fenêtre qui ne fait
        // que raccourcir ne doit rien dire du tout.
        var shortening = await handler.Handle(
            new PreviewPromotionPauseQuery(
                TestHarness.LevelId, new DateOnly(2026, 1, 26), new DateOnly(2026, 1, 30), "Examens"),
            default);

        shortening.Value.SlotsSpanning.Should().Be(1, "seul le créneau large atteint cette semaine");
        shortening.Value.SlotsEmptied.Should().Be(0);
        shortening.Value.CellsInEmptiedSlots.Should().Be(0);
        shortening.Value.Warnings.Should().NotContain(w => w.Contains("perdent la totalité"));
    }
}
