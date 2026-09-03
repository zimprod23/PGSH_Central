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
///
/// <para>⚠ <b>The authority order is covered here whatever the documents are currently allowed to
/// read.</b> <see cref="ServiceChefPolicy.InForce"/> is
/// <see cref="ServiceChefSourcePolicy.SourceNoteOnly"/> while the base's two chef links are test
/// rows, and the handler suites assert that narrowing — so this file is what makes flipping the
/// constant back a one-line change instead of a rediscovery.</para>
/// </summary>
public class ServiceChefDirectoryTests
{
    private const int ServiceId = 7;

    private static readonly DateOnly Autumn = new(2026, 10, 1);
    private static readonly DateOnly Spring = new(2027, 3, 1);

    private static ServiceChefDirectory Directory(
        string? sitting = null,
        string? description = null,
        ServiceChefSourcePolicy policy = ServiceChefSourcePolicy.Authority,
        params ServiceChefTenure[] tenures) =>
        new([new ServiceChefRecord(ServiceId, sitting, description, tenures)], policy);

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

    /// <summary>
    /// ⚠ The policy in force on both documents today: an affectation names a real person and is
    /// still not printed, because the two rows in the base were linked to try the mechanism out.
    /// Neither kind of link answers — the dated tenure is not a stronger claim than the sitting
    /// chef when the objection is that the row itself is test data.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Under_the_note_only_policy_a_linked_chef_is_passed_over(bool dated)
    {
        var directory = Directory(
            sitting: dated ? null : "Nadia Bennis",
            description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
            policy: ServiceChefSourcePolicy.SourceNoteOnly,
            tenures: dated ? [new("Ahmed Settaf", new DateOnly(2026, 9, 1), null)] : []);

        var chef = directory.For(ServiceId, Autumn);

        chef.Name.Should().Be("Pr.A.Settaf");
        chef.FromSourceNote.Should().BeTrue(
            "narrowing the sources does not make an undated note a dated record");
    }

    /// <summary>
    /// What the policy costs, and it is deliberate: a service whose only chef is a link names
    /// nobody. A blank cell says less wrongly than a test account's name, and
    /// <c>ExportLabels.ChefOrigin</c> stays empty with it rather than claiming a source.
    /// </summary>
    [Fact]
    public void Under_the_note_only_policy_a_service_with_no_note_names_nobody()
    {
        var directory = Directory(
            sitting: "Nadia Bennis",
            description: "Service de garde, 3e étage",
            policy: ServiceChefSourcePolicy.SourceNoteOnly,
            tenures: [new("Ahmed Settaf", new DateOnly(2026, 9, 1), null)]);

        directory.For(ServiceId, Autumn).Should().Be(ServiceChefAttribution.Unknown);
    }

    /// <summary>The as-of date decides nothing under the narrowed policy — the note is undated — and
    /// the answer must stay stable across the file rather than varying with the row.</summary>
    [Fact]
    public void Under_the_note_only_policy_the_date_changes_nothing()
    {
        var directory = Directory(
            description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
            policy: ServiceChefSourcePolicy.SourceNoteOnly,
            tenures:
            [
                new("Ahmed Settaf", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)),
                new("Nadia Bennis", new DateOnly(2027, 1, 1), null),
            ]);

        directory.For(ServiceId, Autumn).Should().Be(directory.For(ServiceId, Spring));
    }

    /// <summary>
    /// ⚠ « Il n'y a personne » and « il y a quelqu'un, et ce n'est pas ce nom-là » are different
    /// facts calling for opposite acts — designate a chef, versus wait for the policy — so the
    /// directory answers them separately. Always false under <c>Authority</c>: the order falling
    /// through to the note is the rule working, not a name held back.
    /// </summary>
    [Theory]
    [InlineData(ServiceChefSourcePolicy.SourceNoteOnly, true)]
    [InlineData(ServiceChefSourcePolicy.Authority, false)]
    public void A_linked_chef_the_policy_will_not_print_is_reported_as_withheld(
        ServiceChefSourcePolicy policy, bool expected)
    {
        var directory = Directory(
            description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
            policy: policy,
            tenures: [new("Ahmed Settaf", new DateOnly(2026, 9, 1), null)]);

        directory.HasWithheldLinkedChef(ServiceId, Autumn).Should().Be(expected);
    }

    [Fact]
    public void A_sitting_chef_counts_as_a_withheld_link_too()
    {
        Directory(
                sitting: "Nadia Bennis",
                description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
                policy: ServiceChefSourcePolicy.SourceNoteOnly)
            .HasWithheldLinkedChef(ServiceId, Autumn)
            .Should().BeTrue("the FK is a link like the tenure is, and the policy passes over both");
    }

    /// <summary>A tenure that closed before the date is nobody's current chef, so nothing is being
    /// withheld — reporting one sends somebody looking for a name that does not exist.</summary>
    [Fact]
    public void A_tenure_closed_before_the_date_is_not_withheld()
    {
        Directory(
                description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
                policy: ServiceChefSourcePolicy.SourceNoteOnly,
                tenures: [new("Ahmed Settaf", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))])
            .HasWithheldLinkedChef(ServiceId, Autumn)
            .Should().BeFalse();
    }

    [Fact]
    public void A_service_nobody_linked_withholds_nothing()
    {
        Directory(
                description: ServiceChefSourceNote.Format("Pr.A.Settaf"),
                policy: ServiceChefSourcePolicy.SourceNoteOnly)
            .HasWithheldLinkedChef(ServiceId, Autumn)
            .Should().BeFalse("« désignez un chef » is the right advice on 140 of the 148 services");
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
