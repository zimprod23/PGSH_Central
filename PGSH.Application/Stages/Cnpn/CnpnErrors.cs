using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn;

public static class CnpnErrors
{
    public static Error NoRegistration(Guid studentId) => Error.Problem(
        "Cnpn.NoRegistration",
        $"L'étudiant '{studentId}' n'a aucune inscription — impossible de déterminer son CNPN.");

    /// <summary>
    /// No recorded text reaches back to this intake. Deliberately an error rather than a silent
    /// fallback to the newest version: guessing here would put an old student under a CNPN that
    /// shortens their degree.
    /// </summary>
    public static Error NoVersionForIntake(AcademicProgram program, DateOnly entryStart) => Error.Problem(
        "Cnpn.NoVersionForIntake",
        $"Aucun CNPN enregistré pour la filière {program} couvrant une entrée en {entryStart:yyyy}.");

    public static Error VersionNotFound(int cnpnVersionId) => Error.NotFound(
        "CnpnVersions.NotFound",
        $"Aucun CNPN enregistré sous l'identifiant {cnpnVersionId}.");

    public static Error TargetProgramMismatch(
        string code, AcademicProgram textProgram, AcademicProgram criteriaProgram) => Error.Validation(
        "Cnpn.TargetProgramMismatch",
        $"Le CNPN {code} relève de la filière {textProgram} ; le ciblage vise {criteriaProgram}.");

    /// <summary>
    /// Targeting students at a text that governs no intake would leave them under a citation rather
    /// than a rule — arrêté 2175.22 is exactly such a text.
    /// </summary>
    public static Error TargetTextGovernsNoIntake(string code) => Error.Validation(
        "Cnpn.TargetTextGovernsNoIntake",
        $"Le CNPN {code} ne régit aucune promotion : renseignez son année d'entrée en vigueur avant "
        + "d'y rattacher des étudiants.");

    public static readonly Error TargetNothingToApply = Error.Problem(
        "Cnpn.TargetNothingToApply",
        "Aucun étudiant à rattacher : la règle ne retient personne, ou tous sont déjà rattachés.");

    // === Managing the texts themselves ===

    public static Error DuplicateCode(AcademicProgram program, string code) => Error.Conflict(
        "Cnpn.DuplicateCode",
        $"Un CNPN portant la référence « {code} » existe déjà pour la filière {program}.");

    /// <summary>
    /// Two texts of one programme claiming the same first intake makes version selection ambiguous:
    /// <c>CnpnAssignment</c> picks the latest intake at or before a student's entry, and a tie has no
    /// defensible winner.
    /// </summary>
    public static Error IntakeYearAlreadyTaken(AcademicProgram program, string otherCode) => Error.Conflict(
        "Cnpn.IntakeYearAlreadyTaken",
        $"Le CNPN {otherCode} régit déjà les entrants de cette année en {program} ; deux textes ne "
        + "peuvent pas se disputer une même promotion.");

    /// <summary>
    /// Shortening a degree below a level that already carries requirements would strand them: the set
    /// exists, and nothing in the programme's span can serve it.
    /// </summary>
    public static Error CannotShortenBelowRecordedLevel(int totalYears, int recordedLevelYear) =>
        Error.Validation(
            "Cnpn.CannotShortenBelowRecordedLevel",
            $"Ce CNPN comporte déjà des exigences pour la {recordedLevelYear}ᵉ année ; il ne peut pas "
            + $"être ramené à {totalYears} années. Retirez d'abord ces exigences.");

    public static readonly Error CloneIntoItself = Error.Validation(
        "Cnpn.CloneIntoItself",
        "Un CNPN ne peut pas être cloné sur lui-même.");

    public static Error CloneProgramMismatch(string from, string to) => Error.Validation(
        "Cnpn.CloneProgramMismatch",
        $"Les CNPN {from} et {to} ne relèvent pas de la même filière.");

    public static readonly Error CloneSourceEmpty = Error.Problem(
        "Cnpn.CloneSourceEmpty",
        "Le CNPN source ne comporte aucune exigence à reprendre.");

    /// <summary>
    /// The hard gate on deletion. `Users.CnpnVersionId` is NO ACTION at the database level, so this
    /// is also what stops a raw foreign-key violation surfacing as a 500 — but the real reason is
    /// that a deleted text would leave those students following no CNPN at all, which is not a state
    /// the rest of the model can answer questions in.
    /// </summary>
    public static Error CannotDeleteWithStudents(string code, int studentCount) => Error.Conflict(
        "Cnpn.CannotDeleteWithStudents",
        $"{studentCount} étudiant(s) relèvent du CNPN {code}. Rattachez-les à un autre texte avant "
        + "de le supprimer — sans cela ils ne relèveraient plus d'aucun CNPN.");
}
