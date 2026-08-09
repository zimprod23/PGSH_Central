using FluentAssertions;
using PGSH.Application.Stages.Repartition;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Médecine P1 and Chirurgie P1 are independent rows with independent dates — nothing in the schema
/// says they are the same window, and neither guard notices when they drift apart. This is the only
/// thing that does.
/// </summary>
public class PeriodAxisDiagnosticsTests
{
    private static (int, string, DateOnly, DateOnly) Slot(
        int period, string stage, string start, string end) =>
        (period, stage, DateOnly.Parse(start), DateOnly.Parse(end));

    [Fact]
    public void Stages_agreeing_on_every_window_report_nothing()
    {
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Médecine",  "2025-10-01", "2025-10-31"),
            Slot(1, "Chirurgie", "2025-10-01", "2025-10-31"),
            Slot(2, "Médecine",  "2025-11-01", "2025-11-30"),
            Slot(2, "Chirurgie", "2025-11-01", "2025-11-30"),
        ]);

        found.Should().BeEmpty();
    }

    [Fact]
    public void A_two_day_overrun_is_caught_even_though_the_axis_absorbs_it()
    {
        // The dangerous shape: Chirurgie's window strictly contains Médecine's, so PeriodAxis treats it
        // as a composite and drops it, and the printed table looks perfectly normal.
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Médecine",  "2025-10-01", "2025-10-31"),
            Slot(1, "Chirurgie", "2025-10-01", "2025-11-02"),
        ]);

        found.Should().ContainSingle();
        found[0].PeriodNumber.Should().Be(1);
        found[0].Windows.Should().HaveCount(2);
        found[0].Windows.Should().Contain(w => w.Contains("Chirurgie") && w.Contains("02/11"));
    }

    [Fact]
    public void Each_disagreeing_window_names_the_stages_that_declare_it()
    {
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Médecine",   "2025-10-01", "2025-10-31"),
            Slot(1, "Pédiatrie",  "2025-10-01", "2025-10-31"),
            Slot(1, "Chirurgie",  "2025-10-01", "2025-11-30"),
        ]);

        found.Should().ContainSingle();
        found[0].Windows.Should().HaveCount(2);
        // The two that agree are named together, so the odd one out is obvious at a glance.
        found[0].Windows[0].Should().Contain("Médecine, Pédiatrie");
        found[0].Windows[1].Should().Contain("Chirurgie");
    }

    [Fact]
    public void Only_the_periods_that_disagree_are_reported()
    {
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Médecine",  "2025-10-01", "2025-10-31"),
            Slot(1, "Chirurgie", "2025-10-01", "2025-10-31"),
            Slot(2, "Médecine",  "2025-11-01", "2025-11-30"),
            Slot(2, "Chirurgie", "2025-11-01", "2025-12-31"),
        ]);

        found.Should().ContainSingle().Which.PeriodNumber.Should().Be(2);
    }

    [Fact]
    public void A_stage_alone_on_a_period_number_never_disagrees_with_itself()
    {
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Médecine", "2025-10-01", "2025-10-31"),
            Slot(2, "Médecine", "2025-11-01", "2025-11-30"),
        ]);

        found.Should().BeEmpty();
    }

    [Fact]
    public void A_legitimate_length_difference_is_reported_too_because_code_cannot_tell_them_apart()
    {
        // Med6: Chirurgie changes service every two months, ANES REA every month. Both call it P1, both
        // are correct. Reported anyway — distinguishing this from a typo is the human's job.
        var found = PeriodAxisDiagnostics.Find([
            Slot(1, "Chirurgie", "2025-10-01", "2025-11-30"),
            Slot(1, "ANES REA",  "2025-10-01", "2025-10-31"),
        ]);

        found.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_declared_reports_nothing()
    {
        PeriodAxisDiagnostics.Find([]).Should().BeEmpty();
    }
}
