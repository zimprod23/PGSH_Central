using FluentAssertions;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Domain.Hospitals;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Who a document names as a service's chef, and on what authority.
///
/// <para>The order was a private method inside <c>GetLevelRepartitionQueryHandler</c> until the stage
/// export needed the same answer. ⚠ Two documents of one faculty disagreeing about who leads a
/// service is the drift <c>StageScoring</c> and <c>ServicePeriodLifecycle</c> exist to prevent — so
/// the rule moved out, and it is tested where it lives.</para>
///
/// <para>Pure: <see cref="ServiceChefDirectory"/> takes no store and no clock, so every case here is
/// exact.</para>
/// </summary>
public class ServiceChefDirectoryTests
{
    private const int ServiceId = 7;

    private static readonly DateOnly Autumn = new(2026, 10, 1);
    private static readonly DateOnly Spring = new(2027, 3, 1);

    private static ServiceChefDirectory Directory(
        string? sitting = null,
        string? description = null,
        params ServiceChefTenure[] tenures) =>
        new([new ServiceChefRecord(ServiceId, sitting, description, tenures)]);

    /// <summary>
    /// ⚠ The whole reason the as-of date is asked per question rather than per file: a document
    /// covering a year of rotations spans months, and a chef who took over in January did not lead
    /// the students who stood there in October.
    /// </summary>
    [Fact]
    public void The_tenure_open_on_the_date_is_the_one_that_answers()
    {
        var directory = Directory(
            sitting: "Nadia Bennis",
            tenures:
            [
                new("Ahmed Settaf", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)),
                new("Nadia Bennis", new DateOnly(2027, 1, 1), null),
            ]);

        directory.For(ServiceId, Autumn).Name.Should().Be("Ahmed Settaf");
        directory.For(ServiceId, Spring).Name.Should().Be("Nadia Bennis");
    }

    /// <summary>A dated tenure is the record; the sitting chef is only the fallback for services
    /// whose trail predates the audit trail.</summary>
    [Fact]
    public void A_dated_tenure_beats_the_sitting_chef()
    {
        var directory = Directory(
            sitting: "Nadia Bennis",
            tenures: [new("Ahmed Settaf", new DateOnly(2026, 9, 1), null)]);

        var chef = directory.For(ServiceId, Autumn);

        chef.Name.Should().Be("Ahmed Settaf");
        chef.FromSourceNote.Should().BeFalse();
    }

    [Fact]
    public void With_no_tenure_covering_the_date_the_sitting_chef_answers()
    {
        var directory = Directory(
            sitting: "Nadia Bennis",
            tenures: [new("Ahmed Settaf", new DateOnly(2027, 6, 1), null)]);

        directory.For(ServiceId, Autumn).Name.Should().Be("Nadia Bennis");
    }

    /// <summary>
    /// ⚠ The case that governs 140 of the 148 imported services: the Access base named the professor
    /// as free text and nothing else, so this is the only name available — and it is
    /// <b>undated</b>, which is why it is flagged rather than blended in.
    /// </summary>
    [Fact]
    public void The_legacy_note_answers_last_and_says_so()
    {
        var chef = Directory(description: ServiceChefSourceNote.Format("Pr.A.Settaf"))
            .For(ServiceId, Autumn);

        chef.Name.Should().Be("Pr.A.Settaf");
        chef.FromSourceNote.Should().BeTrue(
            "printing an undated import note as though it were the record is a claim nothing supports");
    }

    [Fact]
    public void A_configured_chef_is_never_reported_as_coming_from_the_note()
    {
        var chef = Directory(
                sitting: "Nadia Bennis",
                description: ServiceChefSourceNote.Format("Pr.A.Settaf"))
            .For(ServiceId, Autumn);

        chef.Name.Should().Be("Nadia Bennis");
        chef.FromSourceNote.Should().BeFalse();
    }

    /// <summary>A description that is not a chef note is not a chef. The prefix is the whole
    /// contract, and <c>ServiceChefSourceNote</c> owns it at both ends.</summary>
    [Fact]
    public void A_description_that_carries_no_note_names_nobody()
    {
        var chef = Directory(description: "Service de garde, 3e étage").For(ServiceId, Autumn);

        chef.Name.Should().BeNull();
        chef.FromSourceNote.Should().BeFalse();
    }

    /// <summary>A service outside the directory is « nobody named », not a crash — the export asks
    /// for every service its périodes touch, and a période of a deleted service must still print.</summary>
    [Fact]
    public void A_service_nobody_asked_for_answers_unknown()
    {
        Directory().For(serviceId: 999, Autumn).Should().Be(ServiceChefAttribution.Unknown);
    }

    /// <summary>Two tenures open on one date is a data defect; the later one is the better guess
    /// about which replaced which, and a document must still print exactly one name.</summary>
    [Fact]
    public void Overlapping_tenures_resolve_to_the_most_recently_opened()
    {
        var directory = Directory(tenures:
        [
            new("Ahmed Settaf", new DateOnly(2026, 1, 1), null),
            new("Nadia Bennis", new DateOnly(2026, 6, 1), null),
        ]);

        directory.For(ServiceId, Autumn).Name.Should().Be("Nadia Bennis");
    }

    /// <summary>An <c>Employee</c> with no name yields « " " » from the projection's concatenation,
    /// which is not a chef — it must fall through to the note rather than print a blank.</summary>
    [Fact]
    public void A_blank_name_is_not_a_chef()
    {
        var chef = Directory(
                sitting: "  ",
                description: ServiceChefSourceNote.Format("Pr.A.Settaf"))
            .For(ServiceId, Autumn);

        chef.Name.Should().Be("Pr.A.Settaf");
        chef.FromSourceNote.Should().BeTrue();
    }
}
