using FluentAssertions;
using PGSH.Domain.Calendar;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// A promotion's own exam window. Pure — no store, no clock — so the boundary cases are exact rather
/// than approximately seeded, the way <c>WorkingDayCalendarTests</c> settles jours ouvrables.
///
/// <para>The property the whole design rests on is <see cref="Declaring_the_same_window_twice_gives_the_same_days"/>:
/// a window is <b>derived from</b>, never added to. <c>InternshipAssignment.ResumePeriod</c> accumulates,
/// which is why it cannot be corrected or revoked and this can.</para>
/// </summary>
public class PromotionPauseTests
{
    private static readonly DateTime RecordedOn = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static AcademicYear Year() => new()
    {
        Id = 1,
        Label = "2026-2027",
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2027, 8, 31),
    };

    private static Level Promotion() =>
        new() { Id = 3, Label = "3ème année", Year = 3, AcademicProgram = AcademicProgram.Medecine };

    /// <summary>« Retrait »: a withdrawal marker the import kept as a level. Year 0.</summary>
    private static Level Marker() =>
        new() { Id = 99, Label = "Retrait", Year = 0, AcademicProgram = AcademicProgram.Medecine };

    private static Result<PromotionPause> Declare(
        DateOnly start, DateOnly end, Level? level = null, string reason = "Examens du 1er semestre") =>
        PromotionPause.Declare(
            level ?? Promotion(), Year(), start, end, PauseKind.Exam, reason, true, RecordedOn);

    [Fact]
    public void A_declared_window_closes_its_days_for_the_promotion()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        // Monday 11 → Friday 15 January 2027: five worked days, gone.
        var calendar = WorkingDayCalendar.WeekendsOnly().With(pause);

        calendar.Count(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Should().Be(0);
        calendar.IsWorkingDay(new DateOnly(2027, 1, 13)).Should().BeFalse();
        calendar.IsWorkingDay(new DateOnly(2027, 1, 18)).Should().BeTrue();
    }

    /// <summary>
    /// The property that makes the act correctable and revocable: the dates a calendar yields are a
    /// function of the window, not of how many times it was applied.
    /// </summary>
    [Fact]
    public void Declaring_the_same_window_twice_gives_the_same_days()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;
        var start = new DateOnly(2027, 1, 4);

        var once = WorkingDayCalendar.WeekendsOnly().With(pause);
        var twice = once.With(pause);

        once.Lay(start, 15)!.End.Should().Be(twice.Lay(start, 15)!.End);
    }

    /// <summary>
    /// What the window is <em>for</em>: a column of fifteen worked days laid across it ends a week later
    /// on the wall calendar and still holds fifteen worked days. Nothing was pushed — the days were
    /// simply never counted.
    /// </summary>
    [Fact]
    public void A_column_laid_across_the_window_steps_over_it()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        var without = WorkingDayCalendar.WeekendsOnly();
        var with = without.With(pause);
        var start = new DateOnly(2027, 1, 4);

        without.Lay(start, 15)!.End.Should().Be(new DateOnly(2027, 1, 22));
        with.Lay(start, 15)!.End.Should().Be(new DateOnly(2027, 1, 29));
        with.Lay(start, 15)!.WorkingDays.Should().Be(15);
    }

    [Fact]
    public void A_window_names_the_promotion_that_declared_it()
    {
        ICalendarClosure pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        pause.Scope.Should().Be(CalendarClosureScope.Promotion);
        pause.Name.Should().Be("Examens du 1er semestre");
    }

    /// <summary>
    /// A pause is not evidence that the faculty's own calendar is complete. Named « Aïd al-Fitr » it
    /// would otherwise silence the one report that says a lunar date is still missing.
    /// </summary>
    [Fact]
    public void A_pause_never_stands_in_for_a_missing_religious_holiday()
    {
        var pause = Declare(new DateOnly(2027, 3, 19), new DateOnly(2027, 3, 20),
            reason: "Aïd al-Fitr").Value;

        WorkingDayCalendar.Build([pause])
            .MissingReligious(new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31))
            .Should().Contain("Aïd al-Fitr");
    }

    [Fact]
    public void The_withdrawal_marker_sits_no_exams()
    {
        var result = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15), Marker());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotAPromotion");
    }

    [Fact]
    public void A_window_cannot_end_before_it_starts()
    {
        var result = Declare(new DateOnly(2027, 1, 15), new DateOnly(2027, 1, 11));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PromotionPauses.EndsBeforeItStarts");
    }

    [Fact]
    public void A_window_says_why()
    {
        Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15), reason: "   ")
            .Error.Code.Should().Be("PromotionPauses.ReasonRequired");
    }

    /// <summary>
    /// Beyond a term the right row is a faculty <c>Holiday</c>: a promotion-scoped closure that long
    /// makes every stage of that promotion unmeasurable, silently.
    /// </summary>
    [Fact]
    public void A_window_longer_than_a_term_is_refused()
    {
        var start = new DateOnly(2026, 9, 1);

        Declare(start, start.AddDays(PromotionPause.MaxSpanDays)).Error.Code
            .Should().Be("PromotionPauses.SpanTooLong");

        Declare(start, start.AddDays(PromotionPause.MaxSpanDays - 1)).IsSuccess
            .Should().BeTrue("the ceiling is inclusive");
    }

    [Fact]
    public void A_window_falls_inside_the_year_it_names()
    {
        // 2026-2027 runs 01/09/2026 → 31/08/2027.
        Declare(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 5)).Error.Code
            .Should().Be("PromotionPauses.OutsideAcademicYear");

        Declare(new DateOnly(2027, 8, 25), new DateOnly(2027, 9, 5)).Error.Code
            .Should().Be("PromotionPauses.OutsideAcademicYear");
    }

    [Fact]
    public void Correcting_the_dates_moves_the_window_and_says_so()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        var corrected = pause.Correct(
            Year(), new DateOnly(2027, 1, 12), new DateOnly(2027, 1, 16), PauseKind.Exam,
            "Examens du 1er semestre", true);

        corrected.IsSuccess.Should().BeTrue();
        corrected.Value.DatesMoved.Should().BeTrue();
        corrected.Value.PreviousStart.Should().Be(new DateOnly(2027, 1, 11));
        pause.StartDate.Should().Be(new DateOnly(2027, 1, 12));
    }

    /// <summary>
    /// The common correction — confirming dates that were already right. It costs no worked day, and
    /// saying otherwise would train the reader to dismiss the report that matters.
    /// </summary>
    [Fact]
    public void Confirming_a_window_that_did_not_move_reports_no_movement()
    {
        var pause = PromotionPause.Declare(
            Promotion(), Year(), new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15),
            PauseKind.Exam, "Examens", isConfirmed: false, RecordedOn).Value;

        pause.WouldMove(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Should().BeFalse();

        var corrected = pause.Correct(
            Year(), new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15), PauseKind.Exam,
            "Examens", isConfirmed: true);

        corrected.Value.DatesMoved.Should().BeFalse();
        pause.IsConfirmed.Should().BeTrue();
    }

    /// <summary>A correction is held to the same rules as a declaration, or the row is unsaveable.</summary>
    [Fact]
    public void A_correction_cannot_push_the_window_out_of_its_year()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        var corrected = pause.Correct(
            Year(), new DateOnly(2027, 8, 28), new DateOnly(2027, 9, 3), PauseKind.Exam, "Examens", true);

        corrected.IsFailure.Should().BeTrue();
        corrected.Error.Code.Should().Be("PromotionPauses.OutsideAcademicYear");
        pause.StartDate.Should().Be(new DateOnly(2027, 1, 11), "a refused correction moves nothing");
    }

    [Fact]
    public void Declaring_announces_it()
    {
        var pause = Declare(new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 15)).Value;

        pause.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PromotionPauseDeclaredDomainEvent>()
            .Which.LevelLabel.Should().Be("3ème année");
    }

    [Fact]
    public void A_window_over_a_weekend_only_costs_nothing()
    {
        var pause = Declare(new DateOnly(2027, 1, 16), new DateOnly(2027, 1, 17)).Value;

        // Saturday and Sunday: the window is real, and it takes no day of stage with it.
        WorkingDayCalendar.WeekendsOnly()
            .Count(pause.StartDate, pause.EndDate).Should().Be(0);
    }
}
