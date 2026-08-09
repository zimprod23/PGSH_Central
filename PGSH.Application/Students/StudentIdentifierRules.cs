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
///   <item>4,695 <c>LEGACY-nnnnn</c> placeholders the Access import manufactured for rows whose CNE
///   the source never recorded (see <c>LegacyIdentityMapper.SyntheticCnePrefix</c>);</item>
///   <item>835 digits-only codes of 8–10 digits;</item>
///   <item>faculty-issued codes such as <c>22FMPR1444</c> and <c>USMBA21194</c>;</item>
///   <item>codes carrying an internal space, e.g. <c>R 13089613</c>.</item>
/// </list>
/// So the code is an identifier of external provenance, and PGSH is not the authority on its
/// grammar. What PGSH does enforce is that it is present, plausibly a code, and unique — uniqueness
/// is the constraint that actually protects anything here.
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

    public static IRuleBuilderOptions<T, string> ValidCne<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Matches(CnePattern)
            .WithMessage(CneMessage);
}
