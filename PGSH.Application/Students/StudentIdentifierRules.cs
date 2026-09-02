using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;

namespace PGSH.Application.Students;

/// <summary>
/// The shape of a student's national code, stated once so create and edit cannot disagree — a rule
/// enforced on one path only is a student who can be created and then never saved again.
///
/// ⚠ <b>It is deliberately a format check, not a shape check.</b> An earlier
/// <c>^[A-Z]\d{6,12}$</c> described the modern CNE correctly and rejected <b>5,646 of the 10,204
/// students actually in the base</b> — which meant editing any of them was impossible, whatever the
/// field being corrected. What is really in there:
/// <list type="bullet">
///   <item>835 digits-only codes of 8–10 digits;</item>
///   <item>faculty-issued codes such as <c>22FMPR1444</c> and <c>USMBA21194</c>;</item>
///   <item>codes carrying an internal space, e.g. <c>R 13089613</c>.</item>
/// </list>
/// So the code is an identifier of external provenance, and PGSH is not the authority on its
/// grammar. What PGSH does enforce is that it is <em>plausibly</em> a code where one is given, and
/// unique — uniqueness is the constraint that actually protects anything here.
///
/// ⚠ <b>And it does not enforce presence.</b> The Access base records a CNE for 5 510 of its 10 203
/// students; the import used to manufacture <c>LEGACY-nnnnn</c> for the remaining 4 695, which put a
/// value that reads exactly like a national code into every screen, every export and every
/// identifier-matching import. <c>Student.CNE</c> is optional, so an absent code is stored absent
/// and every rule here applies <em>when a value is supplied</em>. <see cref="MaxAppogeeLength"/>'s
/// column is the identifier that is in practice always present.
///
/// The handful of rows with encoding damage (<c>ﾞ136627302</c>) still fail, which is right: they are
/// corrupt, and the edit that fixes such a student is the one that retypes the code.
/// </summary>
public static class StudentIdentifierRules
{
    /// <summary>Letters, digits, spaces, hyphens and underscores; 3–20 characters; must start alphanumeric.</summary>
    public const string CnePattern = @"^[A-Za-z0-9][A-Za-z0-9 _-]{2,19}$";

    public const string CneMessage =
        "Le CNE/CIN doit comporter de 3 à 20 caractères : lettres, chiffres, espaces, tirets ou underscores.";

    /// <summary>
    /// The rule for a CNE that may legitimately be absent: checked when a value is given, silent
    /// when the field is empty.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Absence is not a validation failure here, and making it one would make ~4 700 imported
    /// students read-only</b> — the exact failure this file was written about, in the other
    /// direction. A required-ness rule belongs to a command that genuinely cannot proceed without
    /// the value, and no command in PGSH is in that position: <c>Appogee</c> identifies a student
    /// wherever a CNE is missing.
    /// </remarks>
    public static IRuleBuilderOptions<T, string?> ValidCne<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(IsAbsentOrValidCne).WithMessage(CneMessage);

    /// <summary>
    /// The same rule, asked directly.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Any path that <em>manufactures</em> a CNE has to ask this before writing it.</b> A
    /// validator describes what a <em>save</em> must satisfy, so a student created with a code this
    /// pattern rejects becomes read-only the moment somebody opens his file — and the refusal names a
    /// field nobody was editing. That has happened twice already (the old CNE regex, and
    /// <c>Objectives.NotEmpty()</c> on the stage form). Refusing at creation is the cheap end.
    /// </remarks>
    public static bool IsValidCne(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, CnePattern);

    /// <summary>
    /// What the validators actually ask: a code that is either not there at all, or well-formed.
    /// </summary>
    /// <remarks>
    /// Blank and whitespace count as absent rather than as a malformed code — a form posts <c>""</c>
    /// for a field nobody filled in, and refusing that would name the CNE on every edit of a student
    /// who has none.
    /// </remarks>
    public static bool IsAbsentOrValidCne(string? value) =>
        string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value, CnePattern);

    /// <summary>
    /// A CNE as it should be stored: trimmed, or <see langword="null"/> when the caller supplied
    /// nothing. <c>IX_Student_CNE</c> is filtered on <c>IS NOT NULL</c>, so <c>""</c> is a *value* —
    /// the second student saved with a blank box would collide with the first.
    /// </summary>
    public static string? NormalizeCne(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The column width of <c>Students.Appogee</c> — uniquely indexed, and in practice the identifier
    /// every student actually carries: it holds the legacy <c>NO_ORDRE</c> verbatim for all 10 203
    /// imported rows, and it is the column the faculty's own réinscription file keys on.
    /// </summary>
    public const int MaxAppogeeLength = 50;

    /// <summary>Where an address PGSH had to manufacture lives.</summary>
    public const string DefaultEmailDomain = "um5.ac.ma";

    /// <summary>
    /// The local part of a manufactured address — <c>prenom_nom</c>, lower-cased, unaccented, letters
    /// only. Empty when the name yields nothing, which the caller answers from whatever identifier it
    /// has.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>One rule, one place, because there are two generators.</b> <c>LegacyIdentityMapper</c>
    /// manufactured all 10 204 imported addresses and <c>InscriptionPlanner</c> manufactures every new
    /// one; a second copy that quietly kept digits as well as letters would give one faculty two
    /// address namespaces, and re-running the import — which Phase 16 plans — would renumber people
    /// who already log in. <b>Letters only is the behaviour already on disk</b>, so it is the
    /// behaviour this states.
    /// </remarks>
    public static string EmailLocalPart(string? firstName, string? lastName) =>
        $"{Slug(firstName)}_{Slug(lastName)}".Trim('_');

    /// <summary>The nᵗʰ candidate address for a local part — <c>n = 0</c> is the unsuffixed one.</summary>
    public static string EmailCandidate(string localPart, int index, string domain = DefaultEmailDomain) =>
        index == 0 ? $"{localPart}@{domain}" : $"{localPart}{index + 1}@{domain}";

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetter(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
