using System.Globalization;
using PGSH.Application.Students;
using PGSH.Domain.Users;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Turns a legacy `ETUDIANT` row into the identity fields PGSH requires but Access never stored.
///
/// Two of those fields are NOT NULL UNIQUE in PGSH and simply absent from the source:
/// <list type="bullet">
///   <item><b>Email</b> — no column at all. Generated as <c>prenom_nom@um5.ac.ma</c>.</item>
///   <item><b>CNE</b> — present on only 5,510 of 10,203 rows, with one junk duplicate.</item>
/// </list>
/// Both fall back to a value derived from <c>NO_ORDRE</c>, which is the legacy primary key and
/// therefore unique and stable. <c>Appogee</c> carries <c>NO_ORDRE</c> verbatim, so every imported
/// student can be traced back to their source row.
/// </summary>
public sealed class LegacyIdentityMapper(string emailDomain = LegacyIdentityMapper.DefaultDomain)
{
    public const string DefaultDomain = StudentIdentifierRules.DefaultEmailDomain;

    /// <summary>Marks a CNE that was manufactured because the source had none — never a real code.</summary>
    public const string SyntheticCnePrefix = "LEGACY-";

    // Allocation is order-dependent (the 2nd "omar_bennani" gets the 2 suffix), so callers MUST feed
    // students in a stable order — NO_ORDRE. Re-running the import in a different order would
    // otherwise hand one person the address another already logs in with.
    private readonly Dictionary<string, int> _taken = new(StringComparer.Ordinal);

    public LegacyIdentity Map(Legacy.LegacyStudent student)
    {
        var (firstName, lastName) = SplitName(student.Nom);

        // The slug rule is shared with the inscription import, which manufactures addresses for every
        // student enrolled from now on: two copies would give one faculty two address namespaces, and
        // a re-import under the other copy would renumber people who already log in.
        string local = StudentIdentifierRules.EmailLocalPart(firstName, lastName);
        if (local.Length == 0) local = $"etudiant{student.NoOrdre}";

        int seen = _taken.TryGetValue(local, out int count) ? count : 0;
        _taken[local] = seen + 1;

        string email = StudentIdentifierRules.EmailCandidate(local, seen, emailDomain);

        return new LegacyIdentity(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Cne: NormalizeCne(student.Cne, student.NoOrdre),
            Appogee: student.NoOrdre.ToString(CultureInfo.InvariantCulture),
            Gender: MapGender(student.Sexe),
            IsCneSynthetic: !IsUsableCne(student.Cne));
    }

    /// <summary>
    /// Names are one field, surname first: <c>"RHAILI KAMAL"</c> → Kamal Rhaili. The last token is
    /// taken as the given name, which is right for the 7,473 two-token names but genuinely
    /// undecidable for the 2,730 with three or more — <c>"EL IMRANI EL IDRISSI ASMAA"</c> (compound
    /// surname) and <c>"MLIHA TOUATI MOHAMED YASSINE"</c> (compound given name) look identical here.
    /// The whole string is preserved across the two columns, so nothing is lost, only mis-split.
    /// </summary>
    public static (string FirstName, string LastName) SplitName(string? nom)
    {
        var tokens = (nom ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Length switch
        {
            0 => ("", ""),
            1 => ("", Title(tokens[0])),
            _ => (Title(tokens[^1]), Title(string.Join(' ', tokens[..^1]))),
        };
    }

    private static string NormalizeCne(string? cne, int noOrdre) =>
        IsUsableCne(cne) ? cne!.Trim() : $"{SyntheticCnePrefix}{noOrdre}";

    // '########' appears twice and is a placeholder somebody typed, not a national code.
    private static bool IsUsableCne(string? cne) =>
        !string.IsNullOrWhiteSpace(cne) && cne.Trim().Any(char.IsLetterOrDigit);

    private static Gender MapGender(string? sexe) => sexe?.Trim().ToUpperInvariant() switch
    {
        "M" => Gender.Male,
        "F" => Gender.Female,
        // 1,050 rows are blank and 3 hold "C". None is the honest answer; it is not a guess.
        _   => Gender.None,
    };

    private static string Title(string value) =>
        CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(value.ToLowerInvariant());

}

public sealed record LegacyIdentity(
    string FirstName,
    string LastName,
    string Email,
    string Cne,
    string Appogee,
    Gender Gender,
    bool IsCneSynthetic);
