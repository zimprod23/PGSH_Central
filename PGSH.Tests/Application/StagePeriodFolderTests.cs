using FluentAssertions;
using PGSH.Application.Stages.Export;
using PGSH.Domain.Calendar;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// How several périodes of one stage become one line of a document.
///
/// <para>The rule under test: a <b>stay</b> is a maximal run of périodes in the same service with no
/// worked day between them, one stay prints as one span, and the multi-période fact is carried by a
/// number rather than by the shape of a string. The interesting cases are the ones where the naive
/// answer (« print début → fin ») is wrong, and where the cautious answer (« always print every
/// window ») is noise.</para>
///
/// <para>Pure — no store, no clock — so every case is exact rather than approximately seeded.</para>
/// </summary>
public class StagePeriodFolderTests
{
    private static readonly WorkingDayCalendar Weekends = WorkingDayCalendar.WeekendsOnly();

    private static ExportedPeriod Period(
        string start, string end, int serviceId = 1, string serviceName = "Cardiologie") =>
        new(Guid.NewGuid(), DateOnly.Parse(start), DateOnly.Parse(end), serviceId, serviceName);

    [Fact]
    public void No_period_is_not_a_zero_length_stage()
    {
        var summary = StagePeriodFolder.Fold([], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.None);
        summary.PeriodCount.Should().Be(0);
        summary.Start.Should().BeNull();
        summary.ShapeText.Should().Be("Aucune période");
    }

    [Fact]
    public void One_period_prints_one_span_and_one_service()
    {
        var summary = StagePeriodFolder.Fold([Period("2025-01-01", "2025-02-01")], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.Single);
        summary.PeriodCount.Should().Be(1);
        summary.ServiceCount.Should().Be(1);
        summary.PeriodsText.Should().Be("01/01/2025 – 01/02/2025");
        summary.ServicesText.Should().Be("Cardiologie");
    }

    /// <summary>
    /// The case that prompted the feature: 01/01→01/02 then 02/02→02/03, one service, meeting end to
    /// end. Printed as two windows it is noise; printed as one span it is exactly true.
    /// </summary>
    [Fact]
    public void Two_periods_meeting_end_to_end_in_one_service_print_as_one_span()
    {
        var summary = StagePeriodFolder.Fold(
            [Period("2025-01-01", "2025-02-01"), Period("2025-02-02", "2025-03-02")], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.SingleServiceContiguous);
        summary.Stays.Should().HaveCount(1);
        summary.PeriodsText.Should().Be("01/01/2025 – 02/03/2025");
        summary.ServicesText.Should().Be("Cardiologie",
            "one service written once — repeating the name either side of an arrow reads as a rotation");

        summary.PeriodCount.Should().Be(2, "the merge must not erase that it was recorded in two");
        summary.ShapeText.Should().Be("Service unique — 2 périodes contiguës");
    }

    /// <summary>
    /// ⚠ A weekend between two windows is how one column follows another — the calendar never lets a
    /// window swallow its trailing rest days. A calendar-day test would call every Friday → Monday
    /// hand-over an interruption.
    /// </summary>
    [Fact]
    public void A_weekend_between_two_periods_is_not_an_interruption()
    {
        // 2025-01-31 is a Friday; 2025-02-03 the Monday after.
        var summary = StagePeriodFolder.Fold(
            [Period("2025-01-06", "2025-01-31"), Period("2025-02-03", "2025-02-28")], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.SingleServiceContiguous);
        summary.PeriodsText.Should().Be("06/01/2025 – 28/02/2025");
    }

    [Fact]
    public void A_declared_holiday_between_two_periods_is_not_an_interruption_either()
    {
        var calendar = WorkingDayCalendar.Build(
        [
            new Holiday
            {
                Name = "Aïd", StartDate = new DateOnly(2025, 2, 3), EndDate = new DateOnly(2025, 2, 4),
                IsConfirmed = true,
            },
        ]);

        // 2025-01-31 Friday, then the Aïd Monday–Tuesday, then work resumes on the Wednesday.
        var summary = StagePeriodFolder.Fold(
            [Period("2025-01-06", "2025-01-31"), Period("2025-02-05", "2025-02-28")], calendar);

        summary.Shape.Should().Be(StagePeriodShape.SingleServiceContiguous);
        summary.PeriodsText.Should().Be("06/01/2025 – 28/02/2025");
    }

    /// <summary>
    /// ⚠ The half a merged span would lie about. Worked days nobody served sit between the two
    /// windows, so « 01/01/2025 – 02/03/2025 » would claim the student stood in a service through
    /// February.
    /// </summary>
    [Fact]
    public void A_real_gap_in_one_service_prints_two_spans_and_says_so()
    {
        var summary = StagePeriodFolder.Fold(
            [Period("2025-01-01", "2025-02-01"), Period("2025-02-17", "2025-03-02")], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.SingleServiceInterrupted);
        summary.ServiceCount.Should().Be(1);
        summary.Stays.Should().HaveCount(2);
        summary.PeriodsText.Should().Be("01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025");
        summary.ShapeText.Should().Be("Service unique — 2 périodes, 1 interruption(s)");
    }

    [Fact]
    public void Two_services_are_an_itinerary_and_print_in_the_order_they_were_served()
    {
        var summary = StagePeriodFolder.Fold(
        [
            Period("2025-01-01", "2025-02-01", serviceId: 1, serviceName: "Cardiologie"),
            Period("2025-02-02", "2025-03-02", serviceId: 2, serviceName: "Pneumologie"),
        ], Weekends);

        summary.Shape.Should().Be(StagePeriodShape.MultiService);
        summary.ServiceCount.Should().Be(2);
        summary.ServicesText.Should().Be("Cardiologie → Pneumologie");
        summary.PeriodsText.Should().Be("01/01/2025 – 01/02/2025 · 02/02/2025 – 02/03/2025",
            "the spans and the services correspond position by position");
        summary.ShapeText.Should().Be("Rotation — 2 services, 2 périodes");
    }

    /// <summary>
    /// Breaking on the service change alone would merge these two Cardiologie windows across the
    /// Pneumologie one in the middle; breaking on the gap alone would lose the rotation entirely.
    /// </summary>
    [Fact]
    public void Returning_to_a_service_is_three_stays_and_still_two_services()
    {
        var summary = StagePeriodFolder.Fold(
        [
            Period("2025-01-01", "2025-01-31", serviceId: 1, serviceName: "Cardiologie"),
            Period("2025-02-01", "2025-02-28", serviceId: 2, serviceName: "Pneumologie"),
            Period("2025-03-01", "2025-03-31", serviceId: 1, serviceName: "Cardiologie"),
        ], Weekends);

        summary.Stays.Should().HaveCount(3);
        summary.ServiceCount.Should().Be(2);
        summary.ServicesText.Should().Be("Cardiologie → Pneumologie → Cardiologie");
        summary.Shape.Should().Be(StagePeriodShape.MultiService);
    }

    /// <summary>
    /// ⚠ Summed over the périodes, never measured end to end. An interrupted stage's span contains
    /// days nobody served, and a duration read off <c>Fin − Début</c> is the number that makes a
    /// 22-jour stage look like a 60-jour one.
    /// </summary>
    [Fact]
    public void Working_days_are_summed_over_the_periods_not_measured_across_the_gap()
    {
        var first = new DateOnly(2025, 1, 6);   // Monday
        var second = new DateOnly(2025, 3, 3);  // Monday, eight weeks later

        var summary = StagePeriodFolder.Fold(
        [
            Period("2025-01-06", "2025-01-10"),
            Period("2025-03-03", "2025-03-07"),
        ], Weekends);

        summary.WorkingDays.Should().Be(10, "two full working weeks, not the two months between them");
        summary.CalendarDays.Should().Be(10);
        summary.Start.Should().Be(first);
        summary.End.Should().Be(second.AddDays(4));
    }

    [Fact]
    public void Periods_out_of_order_are_folded_in_date_order()
    {
        var summary = StagePeriodFolder.Fold(
        [
            Period("2025-02-02", "2025-03-02", serviceId: 2, serviceName: "Pneumologie"),
            Period("2025-01-01", "2025-02-01", serviceId: 1, serviceName: "Cardiologie"),
        ], Weekends);

        summary.ServicesText.Should().Be("Cardiologie → Pneumologie");
        summary.Start.Should().Be(new DateOnly(2025, 1, 1));
    }
}
