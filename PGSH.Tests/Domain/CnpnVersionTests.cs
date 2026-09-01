using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// What a CNPN refuses about itself, exercised on the aggregate rather than through a handler.
///
/// <para>⚠ The distinction is the one <c>RegistrationCnpnStampTests</c> makes, for the same reason:
/// a handler can refuse a case before the aggregate ever sees it, so a test that goes through the
/// handler proves the handler's guard and nothing about the text. These rules used to live in two
/// handlers — one of them stating « ne peut pas être ramené à N années » from the version's side and
/// the other from the level's — which is exactly how one invariant comes to be written twice and
/// then to disagree with itself.</para>
///
/// <para>The rules that need to see the <i>other</i> texts stay with the handler and are covered in
/// <c>CnpnEffectivityTests</c> / <c>CnpnVersionManagementTests</c>: a duplicate code, an intake year
/// already claimed, a (level, year) a rival text already takes effect for.</para>
/// </summary>
public class CnpnVersionTests
{
    private static readonly AcademicYear Year2026 = new()
    {
        Id = 20, Label = "2026-2027",
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
    };

    private static Level LevelOf(int id, int year, AcademicProgram program = AcademicProgram.Medecine) =>
        new() { Id = id, Label = $"{year}ème année", Year = year, AcademicProgram = program };

    private static CnpnVersion SixYearText() => new()
    {
        Id = 92, Code = "1650.25", Label = "CNPN 2025 (6 ans)",
        AcademicProgram = AcademicProgram.Medecine, TotalYears = 6,
    };

    // -- Correcting the text --------------------------------------------------

    [Fact]
    public void A_correction_records_the_new_citation_and_span()
    {
        var text = SixYearText();

        var result = text.Correct("1650.25 bis", "  CNPN 2025 rectifié  ", 6, " BO 7422 ", 12, CnpnSpanFloor.None);

        result.IsSuccess.Should().BeTrue();
        text.Code.Should().Be("1650.25 bis");
        text.Label.Should().Be("CNPN 2025 rectifié", "the label is trimmed, as every stored label is");
        text.Reference.Should().Be("BO 7422");
        text.AppliesToEntrantsFromAcademicYearId.Should().Be(12);
    }

    [Theory]
    [InlineData("", "Cnpn.CodeRequired")]
    [InlineData("   ", "Cnpn.CodeRequired")]
    public void A_text_without_a_citation_is_refused(string code, string expected)
    {
        SixYearText().Correct(code, "CNPN 2025", 6, null, null, CnpnSpanFloor.None)
            .Error.Code.Should().Be(expected);
    }

    [Fact]
    public void A_text_without_a_label_is_refused()
    {
        SixYearText().Correct("1650.25", "  ", 6, null, null, CnpnSpanFloor.None)
            .Error.Code.Should().Be("Cnpn.LabelRequired");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CnpnVersion.MaxTotalYears + 1)]
    public void A_degree_of_no_years_or_of_absurdly_many_is_refused(int totalYears)
    {
        // Checked on the aggregate and not only in the validator: TotalYears answers « est-ce sa
        // dernière année ? » for every student stamped with the text.
        SixYearText().Correct("1650.25", "CNPN 2025", totalYears, null, null, CnpnSpanFloor.None)
            .Error.Code.Should().Be("Cnpn.TotalYearsOutOfRange");
    }

    /// <summary>
    /// Shortening below a level that already carries requirements would strand them: the set exists,
    /// and nothing in the programme's span can serve it.
    /// </summary>
    [Fact]
    public void A_text_cannot_be_shortened_below_a_level_that_already_carries_requirements()
    {
        var text = SixYearText();
        var floor = new CnpnSpanFloor(DeepestRecordedLevelYear: 6, DeepestGoverningLevelYear: 0);

        var result = text.Correct("1650.25", "CNPN 2025", totalYears: 5, null, null, floor);

        result.Error.Code.Should().Be("Cnpn.CannotShortenBelowRecordedLevel");
        text.TotalYears.Should().Be(6, "a refused correction changes nothing");
    }

    /// <summary>
    /// …nor below a level it has been declared to take effect for: the rule would point at a year the
    /// programme no longer has. The mirror of the same rule, and it used to be a second copy in a
    /// second handler.
    /// </summary>
    [Fact]
    public void A_text_cannot_be_shortened_below_a_level_it_takes_effect_for()
    {
        var text = SixYearText();
        var floor = new CnpnSpanFloor(DeepestRecordedLevelYear: 0, DeepestGoverningLevelYear: 6);

        text.Correct("1650.25", "CNPN 2025", totalYears: 5, null, null, floor)
            .Error.Code.Should().Be("Cnpn.CannotShortenBelowEffectiveLevel");
    }

    // -- Declaring which levels it governs ------------------------------------

    [Fact]
    public void Declaring_a_rule_records_it_and_announces_it()
    {
        var text = SixYearText();

        var declared = text.DeclareEffectivity(LevelOf(30, 3), Year2026, "  Après négociation  ", DateTime.UtcNow);

        declared.IsSuccess.Should().BeTrue();
        declared.Value.LevelId.Should().Be(30);
        declared.Value.FromAcademicYearId.Should().Be(Year2026.Id);
        declared.Value.Note.Should().Be("Après négociation");
        declared.Value.Id.Should().Be(0, "the store generates the key — pre-setting it makes EF update, not insert");

        text.LevelEffectivities.Should().ContainSingle();
        text.DomainEvents.OfType<CnpnEffectivityDeclaredDomainEvent>()
            .Should().ContainSingle(e => e.LevelId == 30 && e.FromAcademicYearId == Year2026.Id);
    }

    /// <summary>« Retrait » has nobody to govern — no stages, no cohorts, nothing to rotate.</summary>
    [Fact]
    public void A_rule_on_the_withdrawal_marker_is_refused()
    {
        var text = SixYearText();

        text.DeclareEffectivity(LevelOf(99, 0), Year2026, null, DateTime.UtcNow)
            .Error.Code.Should().Be("Levels.NotAPromotion");

        text.LevelEffectivities.Should().BeEmpty();
        text.DomainEvents.Should().BeEmpty("a refusal announces nothing");
    }

    [Fact]
    public void A_rule_pairing_a_text_with_another_programmes_level_is_refused()
    {
        SixYearText()
            .DeclareEffectivity(LevelOf(61, 2, AcademicProgram.Pharmacie), Year2026, null, DateTime.UtcNow)
            .Error.Code.Should().Be("CnpnEffectivity.ProgramMismatch");
    }

    /// <summary>A six-year text cannot take effect for a seventh year — there is no such year in it.</summary>
    [Fact]
    public void A_rule_beyond_the_texts_span_is_refused()
    {
        SixYearText()
            .DeclareEffectivity(LevelOf(63, 7), Year2026, null, DateTime.UtcNow)
            .Error.Code.Should().Be("Cnpn.CannotShortenBelowEffectiveLevel");
    }

    [Fact]
    public void A_text_cannot_take_effect_twice_for_one_level()
    {
        var text = SixYearText();
        var level = LevelOf(30, 3);
        text.DeclareEffectivity(level, Year2026, null, DateTime.UtcNow);

        var earlier = new AcademicYear
        {
            Id = 19, Label = "2025-2026",
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        };

        text.DeclareEffectivity(level, earlier, null, DateTime.UtcNow)
            .Error.Code.Should().Be("CnpnEffectivity.AlreadyDeclared");

        text.LevelEffectivities.Should().ContainSingle("the second declaration wrote nothing");
    }

    // -- Withdrawing one ------------------------------------------------------

    [Fact]
    public void Withdrawing_a_rule_removes_it_and_announces_it()
    {
        var text = SixYearText();
        var declared = text.DeclareEffectivity(LevelOf(30, 3), Year2026, null, DateTime.UtcNow).Value;
        declared.Id = 7;
        text.ClearDomainEvents();

        var withdrawn = text.WithdrawEffectivity(7);

        withdrawn.IsSuccess.Should().BeTrue();
        withdrawn.Value.Should().BeSameAs(declared, "the caller needs the row to delete it");
        text.LevelEffectivities.Should().BeEmpty();
        text.DomainEvents.OfType<CnpnEffectivityWithdrawnDomainEvent>()
            .Should().ContainSingle(e => e.LevelId == 30);
    }

    [Fact]
    public void Withdrawing_a_rule_this_text_never_declared_is_refused()
    {
        var text = SixYearText();

        text.WithdrawEffectivity(4242)
            .Error.Code.Should().Be("CnpnEffectivity.NotFound");
    }
}
