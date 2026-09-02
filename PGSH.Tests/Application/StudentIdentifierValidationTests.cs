using FluentValidation.TestHelper;
using PGSH.Application.Students.Update;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The CNE is an identifier of external provenance, and PGSH is not the authority on its grammar.
/// The rule it used to enforce — <c>^[A-Z]\d{6,12}$</c> — described the modern code correctly and
/// rejected <b>5,646 of the 10,204 students in the base</b>, which meant those students could not be
/// edited at all, whatever field was being corrected. Every shape below is one that really occurs.
///
/// <para>⚠ <b>And absence is not a shape it refuses.</b> The Access base records a code for 5 510 of
/// its 10 203 students; the import used to manufacture <c>LEGACY-nnnnn</c> for the rest, so the
/// column pretended everyone had one. <c>Student.CNE</c> is optional now, which means a validator
/// asking for presence would make exactly the students without one unsaveable — the same failure this
/// file was written about, from the other side.</para>
/// </summary>
public class StudentIdentifierValidationTests
{
    private static readonly UpdateStudentCommandValidator Validator = new();

    private static UpdateStudentCommand WithCne(string? cne) => new(
        Guid.NewGuid(), "etudiant@um5.ac.ma", "Amine", "Rhaili", null, cne, "2200123",
        AccessGrade: 14, AcademicProgram.Medecine, BacSeries.SVT, "2022",
        Gender.Male, CivilStatus.Civil, NationalityStatus.Marocaine,
        PlaceOfBirth: null, FullAddress: null,
        DateOfBirth: new DateOnly(2000, 1, 1),
        Academy: null, Province: null, Ranking: null);

    [Theory]
    [InlineData("R130012345")]  // the modern CNE: letter + digits
    [InlineData("1234567890")]  // digits only — 835 students carry one
    [InlineData("12345678")]    // an 8-digit legacy code
    [InlineData("22FMPR1444")]  // a faculty-issued code
    [InlineData("USMBA21194")]  // a code issued by another university
    [InlineData("R 13089613")]  // a code recorded with an internal space
    [InlineData("r130012345")]  // lower case — the same code, differently typed
    public void A_code_that_really_occurs_is_accepted(string cne) =>
        Validator.TestValidate(WithCne(cne))
            .ShouldNotHaveValidationErrorFor(x => x.CNE);

    /// <summary>
    /// A student the faculty holds no national code for. The 4 693 imported rows in this position
    /// carry a null CNE and are identified by their numéro Apogée; refusing the field would name it
    /// on every edit of every one of them.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_code_is_accepted(string? cne) =>
        Validator.TestValidate(WithCne(cne))
            .ShouldNotHaveValidationErrorFor(x => x.CNE);

    [Theory]
    [InlineData("R1")]                    // two characters is not an identifier
    [InlineData("ﾞ136627302")]            // encoding damage — the edit that fixes this student retypes the code
    [InlineData("R130012345R130012345R")] // 21 characters, past any real issuer's format
    [InlineData("<script>")]              // punctuation has no place in a national code
    public void A_code_that_is_corrupt_is_refused(string cne) =>
        Validator.TestValidate(WithCne(cne))
            .ShouldHaveValidationErrorFor(x => x.CNE);
}
