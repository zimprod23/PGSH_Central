using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Users;
using PGSH.LegacyImport.Legacy;
using PGSH.LegacyImport.Mapping;
using Xunit;

namespace PGSH.Tests.LegacyImport;

// The pure half of the legacy import. None of this touches Access, which matters: the .mdb holds real
// personal data and is gitignored, so the mapping rules have to be provable without it.
public class LevelMapperTests
{
    [Theory]
    [InlineData("MED01", "MDME1")]     // renamed in 2025/26, same first year of Médecine
    [InlineData("MED02", "MDME2")]
    [InlineData("MDPH01", "MPHAR1")]
    [InlineData("MDPH02", "MPHAR2")]
    public void Renamed_codes_resolve_to_the_same_level(string oldCode, string newCode)
    {
        var previous = LevelMapper.Resolve(oldCode);
        var current = LevelMapper.Resolve(newCode);

        previous.Should().NotBeNull();
        current.Should().NotBeNull();
        (current!.Year, current.Program).Should().Be((previous!.Year, previous.Program));
    }

    [Fact]
    public void Every_level_is_unique_on_year_and_programme()
    {
        // Level carries a UNIQUE(Year, AcademicProgram) index — a duplicate here would fail the insert.
        var levels = LevelMapper.AllLevels();

        levels.Select(l => (l.Year, l.Program)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Medecine_and_pharmacie_years_map_to_their_own_programme()
    {
        LevelMapper.Resolve("MED04").Should().BeEquivalentTo(
            new { Year = 4, Program = AcademicProgram.Medecine });
        LevelMapper.Resolve("MDPH05").Should().BeEquivalentTo(
            new { Year = 5, Program = AcademicProgram.Pharmacie });
    }

    [Fact]
    public void Retrait_is_a_withdrawal_marker_not_a_year_of_study()
    {
        LevelMapper.IsWithdrawal("MED00").Should().BeTrue();
        LevelMapper.IsWithdrawal("MED01").Should().BeFalse();

        // Still resolvable: Registration.LevelId is required, so the 13 rows keep a real level.
        LevelMapper.Resolve("MED00")!.Year.Should().Be(0);
    }

    [Fact]
    public void An_unknown_code_resolves_to_nothing_rather_than_a_guess()
    {
        LevelMapper.Resolve("MED99").Should().BeNull();
        LevelMapper.Resolve(null).Should().BeNull();
    }
}

public class ServiceNameParserTests
{
    [Fact]
    public void The_common_shape_splits_into_hospital_service_and_professor()
    {
        var parsed = ServiceNameParser.Parse("Hôp.IbnSina: Médecine A - Pr.H.Harmouch");

        parsed.Hospital.Name.Should().Be("Hôpital Ibn Sina");
        parsed.Hospital.City.Should().Be("Rabat");
        parsed.Name.Should().Be("Médecine A");
        parsed.ChefName.Should().Be("Pr.H.Harmouch");
    }

    [Fact]
    public void A_missing_space_after_the_colon_still_splits()
    {
        var parsed = ServiceNameParser.Parse("Hôp.IbnSina:Urg.Porte.Chirurgicale - Pr.M.Alilou");

        parsed.Hospital.Name.Should().Be("Hôpital Ibn Sina");
        parsed.Name.Should().Be("Urg.Porte.Chirurgicale");
    }

    [Fact]
    public void The_professor_is_found_even_without_a_dash()
    {
        var parsed = ServiceNameParser.Parse("Hôp.Mly Youssef: Pharmacie Pr.Y.Elalaoui");

        parsed.Name.Should().Be("Pharmacie");
        parsed.ChefName.Should().Be("Pr.Y.Elalaoui");
    }

    [Fact]
    public void Two_professors_stay_one_string_rather_than_truncating_the_service()
    {
        var parsed = ServiceNameParser.Parse("HMIMV: Pharmacie - Pr.Y.Tadlaoui- Pr.A.Elouartiti");

        parsed.Name.Should().Be("Pharmacie");
        parsed.ChefName.Should().Be("Pr.Y.Tadlaoui- Pr.A.Elouartiti");
    }

    [Fact]
    public void A_dash_inside_the_service_name_is_not_mistaken_for_a_separator()
    {
        var parsed = ServiceNameParser.Parse("H.Mat.Souissi: Réa-Obs - Pr.R.Tachinante");

        parsed.Name.Should().Be("Réa-Obs");
        parsed.ChefName.Should().Be("Pr.R.Tachinante");
    }

    [Fact]
    public void Spelling_variants_collapse_onto_one_hospital()
    {
        // Left apart these become two hospitals and the same ward shows up twice in the tree.
        var withHop = ServiceNameParser.Parse("Hôp.Mat.Souissi: Cardiologie B - Pr.Cherti");
        var withH = ServiceNameParser.Parse("H.Mat.Souissi: Méd A - Pr.H.Harmouche");

        withH.Hospital.Name.Should().Be(withHop.Hospital.Name);
    }

    [Fact]
    public void A_dash_separated_hospital_is_recognised_when_the_left_side_looks_like_one()
    {
        var parsed = ServiceNameParser.Parse("Hôp.Azzamouri Kénitra-Chirurgie");

        parsed.Hospital.Name.Should().Be("Hôpital Azzamouri");
        parsed.Hospital.City.Should().Be("Kénitra");
        parsed.Name.Should().Be("Chirurgie");
    }

    [Fact]
    public void A_service_naming_no_hospital_lands_in_the_catch_all()
    {
        var parsed = ServiceNameParser.Parse("Stage délocalisé");

        parsed.Hospital.Name.Should().Be(ServiceNameParser.UnknownHospital);
        parsed.Name.Should().Be("Stage délocalisé");
        parsed.ChefName.Should().BeNull();
    }

    [Fact]
    public void A_service_with_a_professor_but_no_hospital_still_keeps_its_name()
    {
        var parsed = ServiceNameParser.Parse("Santé Publique - Pr.R.Razine");

        parsed.Hospital.Name.Should().Be(ServiceNameParser.UnknownHospital);
        parsed.Name.Should().Be("Santé Publique");
        parsed.ChefName.Should().Be("Pr.R.Razine");
    }

    [Theory]
    [InlineData("Hôp.IbnSina: Chirurgie A - Pr.X", ServiceType.Chirurgie)]
    [InlineData("Hôp.IbnSina: Pharmacie - Pr.X", ServiceType.Biologie)]
    [InlineData("Hôp.IbnSina: Cardiologie A - Pr.X", ServiceType.Medical)]
    public void The_service_type_is_inferred_from_the_name(string raw, ServiceType expected) =>
        ServiceNameParser.Parse(raw).Type.Should().Be(expected);

    // Every one of these was mis-typed by a naive substring classifier, and all are real catalogue
    // entries. "Neurologie" contains "urologie"; "Chirurgicale" does not contain "chirurgie".
    [Theory]
    [InlineData("Hôp.Spécialités: Neurologie A - Pr.S.Aidi", ServiceType.Medical)]
    [InlineData("HMIMV: Néphrologie - Pr.D.Kabbaj", ServiceType.Medical)]
    [InlineData("Hôp.IbnSina: Urologie A - Pr.Y.Nouini", ServiceType.Chirurgie)]
    [InlineData("Hôp.Spécialités: Neuro-chirurgie B - Pr.Y.Arkha", ServiceType.Chirurgie)]
    [InlineData("Hôp.IbnSina:Urg.Porte.Chirurgicale - Pr.M.Alilou", ServiceType.Chirurgie)]
    [InlineData("Hôp.IbnSina: Réanimation chirurgicale - Pr.A.Azzouzi", ServiceType.Chirurgie)]
    [InlineData("Hôp.IbnSina: Urg-Chir-Viscérale - Pr.E.E.Elfaricha", ServiceType.Chirurgie)]
    [InlineData("HMIMV: Hémato-clinique - Pr.K.Doghmi", ServiceType.Medical)]
    [InlineData("Hôp.AlAyachi: Rhumatologie B - Pr.F.Allali", ServiceType.Medical)]
    [InlineData("Hôp.IbnSina: Cardiologie A - Pr.R.Fellat", ServiceType.Medical)]
    public void Look_alike_specialities_are_not_confused(string raw, ServiceType expected) =>
        ServiceNameParser.Parse(raw).Type.Should().Be(expected);

    [Fact]
    public void A_city_named_in_the_source_is_marked_as_stated()
    {
        ServiceNameParser.Parse("Hôp.Azzamouri Kénitra-Chirurgie").Hospital.CityIsStated.Should().BeTrue();

        // Rabat is the documented default, never something the legacy string actually says.
        ServiceNameParser.Parse("Hôp.IbnSina: Médecine A - Pr.X").Hospital.CityIsStated.Should().BeFalse();
    }
}

public class LegacyPeriodParserTests
{
    [Fact]
    public void A_normal_pair_yields_one_window()
    {
        var result = LegacyPeriodParser.Parse("03/11/2025", "02/12/2025");

        result.Unreadable.Should().BeFalse();
        result.IsSplit.Should().BeFalse();
        result.Windows.Should().ContainSingle();
        result.Windows[0].Start.Should().Be(new DateOnly(2025, 11, 3));
        result.Windows[0].End.Should().Be(new DateOnly(2025, 12, 2));
    }

    [Fact]
    public void An_interrupted_rotation_becomes_two_windows()
    {
        // The legacy app could not model a break, so somebody typed the second half into the string.
        var result = LegacyPeriodParser.Parse("22/04/2019", "31/05/2019 & de: 25/06/2019 à:12/07/2019");

        result.IsSplit.Should().BeTrue();
        result.Windows.Should().HaveCount(2);
        result.Windows[0].Should().Be(new LegacyWindow(new DateOnly(2019, 4, 22), new DateOnly(2019, 5, 31)));
        result.Windows[1].Should().Be(new LegacyWindow(new DateOnly(2019, 6, 25), new DateOnly(2019, 7, 12)));
    }

    [Fact]
    public void Fewer_than_two_dates_is_reported_rather_than_invented()
    {
        LegacyPeriodParser.Parse("03/11/2025", null).Unreadable.Should().BeTrue();
        LegacyPeriodParser.Parse(null, null).Unreadable.Should().BeTrue();
    }

    [Fact]
    public void A_window_running_backwards_collapses_instead_of_dropping_the_rotation()
    {
        var result = LegacyPeriodParser.Parse("02/12/2025", "03/11/2025");

        result.Windows.Should().ContainSingle();
        result.Windows[0].End.Should().Be(result.Windows[0].Start);
    }
}

public class LegacyIdentityMapperTests
{
    private static LegacyStudent Student(int noOrdre, string nom, string? cne = null, string? sexe = "M") =>
        new(noOrdre, nom, cne, null, sexe, null, null, null, null, null, null);

    [Fact]
    public void The_last_token_is_taken_as_the_given_name()
    {
        // Names are stored surname-first: "ZERHOUNI NAJAT" is Najat Zerhouni.
        var (first, last) = LegacyIdentityMapper.SplitName("ZERHOUNI NAJAT");

        first.Should().Be("Najat");
        last.Should().Be("Zerhouni");
    }

    [Fact]
    public void A_compound_surname_keeps_all_of_its_tokens()
    {
        var (first, last) = LegacyIdentityMapper.SplitName("EL MANSOURI EL GHAZI IMANE");

        first.Should().Be("Imane");
        last.Should().Be("El Mansouri El Ghazi");
    }

    [Fact]
    public void The_generated_address_is_prenom_underscore_nom()
    {
        var identity = new LegacyIdentityMapper().Map(Student(1, "ZERHOUNI NAJAT"));

        identity.Email.Should().Be("najat_zerhouni@um5.ac.ma");
    }

    [Fact]
    public void Accents_and_punctuation_are_stripped_from_the_address()
    {
        var identity = new LegacyIdentityMapper().Map(Student(1, "BEN-ALI MOHAMMÉD"));

        identity.Email.Should().Be("mohammed_benali@um5.ac.ma");
    }

    [Fact]
    public void A_clashing_address_takes_a_numeric_suffix_in_source_order()
    {
        var mapper = new LegacyIdentityMapper();

        var first = mapper.Map(Student(100, "BOUKHRIS SOFIA"));
        var second = mapper.Map(Student(200, "BOUKHRIS SOFIA"));
        var third = mapper.Map(Student(300, "BOUKHRIS SOFIA"));

        first.Email.Should().Be("sofia_boukhris@um5.ac.ma");
        second.Email.Should().Be("sofia_boukhris2@um5.ac.ma");
        third.Email.Should().Be("sofia_boukhris3@um5.ac.ma");
    }

    [Fact]
    public void Suffix_allocation_is_reproducible_so_a_re_run_cannot_swap_two_people()
    {
        // Email is the login identity. If the suffix moved between runs, one person would end up
        // with the address another already signs in with — hence the fixed NO_ORDRE ordering.
        LegacyIdentity[] Run()
        {
            var mapper = new LegacyIdentityMapper();
            return [.. new[] { Student(100, "BOUKHRIS SOFIA"), Student(200, "BOUKHRIS SOFIA") }
                .OrderBy(s => s.NoOrdre)
                .Select(mapper.Map)];
        }

        Run().Select(i => i.Email).Should().Equal(Run().Select(i => i.Email));
    }

    /// <summary>
    /// ⚠ Nothing is manufactured. The mapper used to write <c>LEGACY-{NO_ORDRE}</c> for the 4 693
    /// source rows carrying no code, which put a value indistinguishable from a real national code
    /// into 46% of the roll — every list, every export, every identifier-matching import.
    /// <c>Student.CNE</c> is optional, so absence is imported as absence and
    /// <see cref="LegacyIdentity.Appogee"/> keeps every student traceable to his source row.
    /// </summary>
    [Fact]
    public void A_missing_cne_is_left_absent_rather_than_manufactured()
    {
        var identity = new LegacyIdentityMapper().Map(Student(9000001, "TALBI RIDA", cne: null));

        identity.CneMissing.Should().BeTrue();
        identity.Cne.Should().BeNull();
        identity.Appogee.Should().Be("9000001", "the legacy key still identifies him");
    }

    /// <summary>
    /// « ######## » appears twice in the source and is a placeholder somebody typed, not a code.
    /// It is absence, not a value to carry across.
    /// </summary>
    [Fact]
    public void A_placeholder_cne_counts_as_absent()
    {
        var identity = new LegacyIdentityMapper().Map(Student(9000002, "TALBI RIDA", cne: "########"));

        identity.CneMissing.Should().BeTrue();
        identity.Cne.Should().BeNull();
    }

    [Fact]
    public void A_real_cne_is_kept_as_is()
    {
        var identity = new LegacyIdentityMapper().Map(Student(1, "ZERHOUNI NAJAT", cne: "1100000099"));

        identity.CneMissing.Should().BeFalse();
        identity.Cne.Should().Be("1100000099");
    }

    [Theory]
    [InlineData("M", Gender.Male)]
    [InlineData("F", Gender.Female)]
    [InlineData("C", Gender.None)]
    [InlineData(null, Gender.None)]
    public void Gender_falls_back_to_None_rather_than_a_guess(string? sexe, Gender expected) =>
        new LegacyIdentityMapper().Map(Student(1, "ZERHOUNI NAJAT", sexe: sexe)).Gender.Should().Be(expected);
}
