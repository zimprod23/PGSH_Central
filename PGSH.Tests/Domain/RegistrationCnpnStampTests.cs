using FluentAssertions;
using PGSH.Domain.Registrations;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// The aggregate's own rule about its governing CNPN, exercised directly rather than through a
/// planner that filters the same case out beforehand.
///
/// <para>⚠ The distinction matters: <c>CnpnEffectivityPlanner</c> already refuses to put a pronounced
/// year into its work list, so a test that goes through it proves the <i>planner</i> guard and
/// nothing about the aggregate. The aggregate's guard is what protects every other caller — the
/// stamper is public, and the ordinary creation paths hand it whatever they built.</para>
/// </summary>
public class RegistrationCnpnStampTests
{
    private static Registration Registration() => new()
    {
        Id = Guid.NewGuid(),
        StudentId = Guid.NewGuid(),
        AcademicYearId = 1,
        LevelId = 1,
    };

    [Fact]
    public void A_first_stamp_records_the_text_and_how_it_was_decided()
    {
        var registration = Registration();

        var result = registration.StampCnpnVersion(92, RegistrationCnpnSource.Effectivity);

        result.IsSuccess.Should().BeTrue();
        registration.CnpnVersionId.Should().Be(92);
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.Effectivity);
    }

    /// <summary>
    /// A verdict is a ruling about a requirement set. Moving that set afterwards leaves nobody able to
    /// say what the jury ruled on, so the aggregate refuses — re-opening the year is the act that
    /// makes such a change legitimate.
    /// </summary>
    [Fact]
    public void A_pronounced_year_refuses_to_change_its_text()
    {
        var registration = Registration();
        registration.StampCnpnVersion(91, RegistrationCnpnSource.StudentStamp);
        registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);

        var result = registration.StampCnpnVersion(92, RegistrationCnpnSource.Effectivity);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.CnpnFrozenByOutcome");
        registration.CnpnVersionId.Should().Be(91);
    }

    /// <summary>
    /// Re-opening the year withdraws the verdict, and with it the freeze. Without this the only way
    /// to correct a genuinely wrong rattachement on a closed year would be SQL.
    /// </summary>
    [Fact]
    public void Reopening_the_year_lifts_the_freeze()
    {
        var registration = Registration();
        registration.StampCnpnVersion(91, RegistrationCnpnSource.StudentStamp);
        registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        registration.ReopenYear("PV corrigé");

        var result = registration.StampCnpnVersion(92, RegistrationCnpnSource.Effectivity);

        result.IsSuccess.Should().BeTrue();
        registration.CnpnVersionId.Should().Be(92);
    }

    /// <summary>
    /// A pronounced year that never carried a text can still receive one: recording what governed it
    /// takes nothing away from the verdict, where changing it would. This is the case the six imported
    /// years are in.
    /// </summary>
    [Fact]
    public void A_pronounced_year_with_no_text_can_still_be_given_one()
    {
        var registration = Registration();
        registration.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Inferred, null, DateTime.UtcNow);

        var result = registration.StampCnpnVersion(91, RegistrationCnpnSource.Backfilled);

        result.IsSuccess.Should().BeTrue();
        registration.CnpnVersionId.Should().Be(91);
    }

    /// <summary>Re-stating the same decision is a no-op, so a re-run of a creation path is safe.</summary>
    [Fact]
    public void Re_stating_the_same_decision_is_a_no_op()
    {
        var registration = Registration();
        registration.StampCnpnVersion(92, RegistrationCnpnSource.Effectivity);
        registration.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);

        var result = registration.StampCnpnVersion(92, RegistrationCnpnSource.Effectivity);

        result.IsSuccess.Should().BeTrue("the freeze protects against change, not against repetition");
    }
}
