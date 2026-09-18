using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Application.Calendar.Pauses;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A declared window is only worth declaring if something says so afterwards.
/// </summary>
/// <remarks>
/// <para><b>What these cover, and why they exist.</b> Reported on the live base 18/09/2026: a window
/// was declared over a promotion that was fully planned and published, and the operator saw « no
/// impact ». Nothing was broken — 10 columns and 1 535 rotations were being cut — but the only screen
/// that said so was the <em>preview</em>, behind an « Aperçu » button that has to be pressed
/// <b>before</b> saving and whose result is cleared by the next keystroke. Declaring a window and then
/// looking at it showed a date range and a cost, and nothing about what it hit.</para>
///
/// <para>⚠ <b>The load-bearing case here is the third one.</b> « 0 rotations crossing » is two opposite
/// facts — the promotion declared its exam weeks and this selection misses them, or <b>nobody declared
/// anything at all</b> — and on this base the second is the ordinary state. A count that cannot tell
/// them apart reads as reassurance when it is ignorance.</para>
/// </remarks>
public class PauseVisibilityTests
{
    private const int OtherLevelId = 7;
    private const int ServiceId = 41;

    private static readonly DateOnly ExamStart = new(2026, 1, 12);
    private static readonly DateOnly ExamEnd = new(2026, 1, 16);

    private static GetPromotionPausesQueryHandler Lister(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new WorkingDayProvider(db));

    private static GetStagePauseCrossingsQueryHandler Crossings(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new WorkingDayProvider(db));

    private static void Declare(
        ApplicationDbContext db, DateOnly start, DateOnly end,
        int levelId = TestHarness.LevelId, string reason = "Examens du 1er semestre") =>
        db.PromotionPauses.Add(new PromotionPause
        {
            AcademicYearId = TestHarness.CurrentYearId,
            LevelId = levelId,
            StartDate = start,
            EndDate = end,
            Kind = PauseKind.Exam,
            Reason = reason,
            IsConfirmed = true,
            RecordedOn = new DateTime(2025, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        });

    /// <summary>
    /// One promotion, one stage, one column laid straight across the exam week, and one rotation on it
    /// that has <b>not</b> been started — i.e. exactly what « Démarrer » is about to act on.
    /// </summary>
    private static (ApplicationDbContext Db, Stage Stage) Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "4ème année", year: 4);

        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6));

        db.SeedPeriod(assignment, service,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6), started: false);

        return (db, stage);
    }

    [Fact]
    public async Task A_listed_window_says_what_it_cuts_and_not_only_what_it_costs()
    {
        var (db, _) = Seed(nameof(A_listed_window_says_what_it_cuts_and_not_only_what_it_costs));
        Declare(db, ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var page = await Lister(db).Handle(new GetPromotionPausesQuery(), default);

        var row = page.Value.Items.Should().ContainSingle().Subject;
        row.WorkingDaysLost.Should().Be(5, "Monday 12 to Friday 16 January 2026");
        row.SlotsSpanning.Should().Be(1, "the column runs 05/01 to 06/02, straight across the window");
        row.PeriodsSpanning.Should().Be(1);

        await db.DisposeAsync();
    }

    /// <summary>
    /// ⚠ The control, and the reason the two numbers are separate: a window costing exactly the same
    /// five days cuts <b>nothing</b> when it lands where the promotion has no plan. Zero here is the
    /// good answer — it is what declaring before laying the axis looks like.
    /// </summary>
    [Fact]
    public async Task A_window_the_plan_does_not_reach_cuts_nothing_while_still_costing_its_days()
    {
        var (db, _) = Seed(nameof(A_window_the_plan_does_not_reach_cuts_nothing_while_still_costing_its_days));
        Declare(db, new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12));
        await db.SaveChangesAsync();

        var page = await Lister(db).Handle(new GetPromotionPausesQuery(), default);

        var row = page.Value.Items.Should().ContainSingle().Subject;
        row.WorkingDaysLost.Should().Be(5);
        row.SlotsSpanning.Should().Be(0);
        row.PeriodsSpanning.Should().Be(0);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task A_listed_window_counts_only_its_own_promotion()
    {
        var (db, _) = Seed(nameof(A_listed_window_counts_only_its_own_promotion));
        Declare(db, ExamStart, ExamEnd, levelId: OtherLevelId, reason: "Examens 4eme");
        await db.SaveChangesAsync();

        var page = await Lister(db).Handle(new GetPromotionPausesQuery(), default);

        var row = page.Value.Items.Should().ContainSingle().Subject;
        row.SlotsSpanning.Should().Be(0, "the column and the rotation belong to the other promotion");
        row.PeriodsSpanning.Should().Be(0);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task The_start_preview_names_the_window_its_selection_runs_into()
    {
        var (db, stage) = Seed(nameof(The_start_preview_names_the_window_its_selection_runs_into));
        Declare(db, ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var result = await Crossings(db).Handle(new GetStagePauseCrossingsQuery(stage.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsToStart.Should().Be(1);
        result.Value.PeriodsCrossing.Should().Be(1);
        result.Value.WindowsDeclaredForPromotion.Should().Be(1);

        var window = result.Value.Windows.Should().ContainSingle().Subject;
        window.Reason.Should().Be("Examens du 1er semestre");
        window.WorkingDaysLost.Should().Be(5);
        window.PeriodsCrossing.Should().Be(1);

        await db.DisposeAsync();
    }

    /// <summary>
    /// ⚠ <b>The distinction the whole response shape exists for.</b> Both of these report « 0 rotations
    /// crossing ». One means the promotion has said when its exams are and this selection misses them;
    /// the other means nobody has said anything, and the zero is measuring an absent faculty document.
    /// <c>WindowsDeclaredForPromotion</c> is what separates them, and without it the safe-looking answer
    /// is the one a promotion with no declared window gives every time.
    /// </summary>
    [Fact]
    public async Task Nothing_crossing_and_nothing_declared_are_not_the_same_answer()
    {
        var (declared, stageA) = Seed("crossing-vs-silence-declared");
        Declare(declared, new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12));
        await declared.SaveChangesAsync();

        var withWindow = await Crossings(declared).Handle(
            new GetStagePauseCrossingsQuery(stageA.Id), default);

        withWindow.Value.PeriodsCrossing.Should().Be(0);
        withWindow.Value.WindowsDeclaredForPromotion.Should()
            .Be(1, "the promotion has said when it sits exams");
        withWindow.Value.Windows.Should().BeEmpty("only crossed windows are listed");

        var (silent, stageB) = Seed("crossing-vs-silence-silent");
        await silent.SaveChangesAsync();

        var withoutWindow = await Crossings(silent).Handle(
            new GetStagePauseCrossingsQuery(stageB.Id), default);

        withoutWindow.Value.PeriodsCrossing.Should().Be(0);
        withoutWindow.Value.WindowsDeclaredForPromotion.Should()
            .Be(0, "nobody has declared anything at all");

        await declared.DisposeAsync();
        await silent.DisposeAsync();
    }

    /// <summary>
    /// A rotation already under way is not something « Démarrer » is about to start, so counting it
    /// would make the warning grow every time the screen was opened.
    /// </summary>
    [Fact]
    public async Task A_rotation_already_started_is_not_about_to_start()
    {
        var (db, stage) = Seed(nameof(A_rotation_already_started_is_not_about_to_start));
        Declare(db, ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        // Through the store rather than the seed helper's flag: the rotation is started *after* the
        // window was declared, which is the order the operator actually works in.
        var period = await db.ServicePeriods.SingleAsync();
        period.IsStarted = true;
        await db.SaveChangesAsync();

        var result = await Crossings(db).Handle(new GetStagePauseCrossingsQuery(stage.Id), default);

        result.Value.PeriodsToStart.Should().Be(0);
        result.Value.PeriodsCrossing.Should().Be(0);
        result.Value.WindowsDeclaredForPromotion.Should()
            .Be(1, "the window is still declared - it is the selection that is empty");

        await db.DisposeAsync();
    }

    /// <summary>
    /// ⚠ Two windows, one rotation spanning both: the rows are per window and the total is per
    /// rotation. Adding the rows would over-count exactly the students worst affected.
    /// </summary>
    [Fact]
    public async Task A_rotation_spanning_two_windows_is_counted_once_in_the_total()
    {
        var (db, stage) = Seed(nameof(A_rotation_spanning_two_windows_is_counted_once_in_the_total));
        Declare(db, ExamStart, ExamEnd, reason: "Examens janvier");
        Declare(db, new DateOnly(2026, 1, 26), new DateOnly(2026, 1, 30), reason: "Rattrapages");
        await db.SaveChangesAsync();

        var result = await Crossings(db).Handle(new GetStagePauseCrossingsQuery(stage.Id), default);

        result.Value.Windows.Should().HaveCount(2);
        result.Value.Windows.Sum(w => w.PeriodsCrossing).Should().Be(2, "one row per window");
        result.Value.PeriodsCrossing.Should().Be(1, "but one rotation, counted once");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task The_preview_narrows_to_the_columns_it_is_given()
    {
        var (db, stage) = Seed(nameof(The_preview_narrows_to_the_columns_it_is_given));
        Declare(db, ExamStart, ExamEnd);
        await db.SaveChangesAsync();

        var elsewhere = await Crossings(db).Handle(
            new GetStagePauseCrossingsQuery(stage.Id, PeriodNumbers: [9]), default);

        elsewhere.Value.PeriodsToStart.Should().Be(0, "the rotation hangs off no column numbered 9");
        elsewhere.Value.PeriodsCrossing.Should().Be(0);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_unknown_stage_is_refused()
    {
        var (db, _) = Seed(nameof(An_unknown_stage_is_refused));
        await db.SaveChangesAsync();

        var result = await Crossings(db).Handle(new GetStagePauseCrossingsQuery(4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotFound(4242));

        await db.DisposeAsync();
    }
}
