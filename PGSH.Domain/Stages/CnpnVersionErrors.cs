using PGSH.SharedKernel;
using PGSH.Domain.Common.Utils;

namespace PGSH.Domain.Stages;

/// <summary>
/// What a <see cref="CnpnVersion"/> refuses about itself and about the levels it declares it
/// governs.
///
/// <para>Separate from the application layer's <c>CnpnErrors</c>, and the split is the one that
/// matters: these are the refusals the text can pronounce from what it holds — its own span, its own
/// requirement sets, its own effectivity rules. The refusals that need to see the <i>other</i> texts
/// (a duplicate code, an intake year already claimed, a level a rival text already takes effect for)
/// cannot be decided here and stay with the handler.</para>
///
/// <para>⚠ Codes are unchanged from where these rules used to live. They are asserted by tests and
/// read by the frontend, and a refusal that renames itself is a refusal nobody handles any more.</para>
/// </summary>
public static class CnpnVersionErrors
{
    public static readonly Error CodeRequired = Error.Validation(
        "Cnpn.CodeRequired",
        "La référence de l'arrêté est obligatoire : c'est ainsi que le texte est cité.");

    public static readonly Error LabelRequired = Error.Validation(
        "Cnpn.LabelRequired",
        "Le libellé du CNPN est obligatoire : c'est ce que les listes affichent.");

    /// <summary>
    /// A degree lasting no years, or more years than any programme runs. Checked here and not only in
    /// the validator because <see cref="CnpnVersion.TotalYears"/> answers « est-ce sa dernière
    /// année ? » for every student stamped with the text.
    /// </summary>
    public static Error TotalYearsOutOfRange(int totalYears, int max) => Error.Validation(
        "Cnpn.TotalYearsOutOfRange",
        $"La durée du cursus doit être comprise entre 1 et {max} années ; {totalYears} a été saisi.");

    /// <summary>
    /// Shortening a degree below a level that already carries requirements would strand them: the set
    /// exists, and nothing in the programme's span can serve it.
    /// </summary>
    public static Error CannotShortenBelowRecordedLevel(int totalYears, int recordedLevelYear) =>
        Error.Validation(
            "Cnpn.CannotShortenBelowRecordedLevel",
            $"Ce CNPN comporte déjà des exigences pour la {recordedLevelYear}ᵉ année ; il ne peut pas "
            + $"être ramené à {totalYears} années. Retirez d'abord ces exigences.");

    /// <summary>
    /// Shortening a text below a level it takes effect for would leave the rule pointing at a year
    /// the programme no longer has.
    /// </summary>
    public static Error CannotShortenBelowEffectiveLevel(int totalYears, int levelYear) =>
        Error.Validation(
            "Cnpn.CannotShortenBelowEffectiveLevel",
            $"Ce CNPN entre en vigueur pour la {levelYear}ᵉ année ; il ne peut pas être ramené à "
            + $"{totalYears} années. Retirez d'abord cette règle d'entrée en vigueur.");

    /// <summary>
    /// A text takes effect for a level once. A second row would say it starts governing that level
    /// twice, which states nothing — correct the existing row instead.
    /// </summary>
    public static Error EffectivityAlreadyDeclared(string code, string levelLabel, string yearLabel) =>
        Error.Conflict(
            "CnpnEffectivity.AlreadyDeclared",
            $"Le CNPN {code} régit déjà {levelLabel} à partir de {yearLabel}. Modifiez cette règle "
            + "plutôt que d'en ajouter une seconde.");

    public static Error EffectivityProgramMismatch(
        string code, AcademicProgram textProgram, string levelLabel, AcademicProgram levelProgram) =>
        Error.Validation(
            "CnpnEffectivity.ProgramMismatch",
            $"Le CNPN {code} relève de la filière {textProgram} ; {levelLabel} relève de {levelProgram}.");

    /// <summary>Withdrawing a rule this text never declared.</summary>
    public static Error EffectivityNotDeclaredHere(string code, int effectivityId) => Error.NotFound(
        "CnpnEffectivity.NotFound",
        $"Le CNPN {code} ne porte aucune règle d'entrée en vigueur sous l'identifiant {effectivityId}.");
}
