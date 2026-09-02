using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn;

/// <summary>
/// What the CNPN <b>handlers</b> refuse — the rules that need to see more than one text, more than
/// one student, or the store itself.
///
/// <para>⚠ The refusals a text can pronounce from what it holds alone live on the aggregate, in
/// <see cref="CnpnVersionErrors"/>: its own span against its own requirement sets and effectivity
/// rules, and which levels it may speak for. Adding one here that the text could have decided is how
/// an invariant ends up stated twice, in two directions, with nothing tying the two together.</para>
/// </summary>
public static class CnpnErrors
{
    /// <summary>
    /// An attribution pass over a database with no academic years at all. Entry is deduced by walking
    /// back through the list of years, so there is nothing to walk — this is a base the import has not
    /// run against yet, not a student PGSH cannot place.
    /// </summary>
    public static readonly Error NoAcademicYears = Error.Problem(
        "Cnpn.NoAcademicYears",
        "Aucune année universitaire enregistrée : l'attribution des CNPN se déduit de la suite des "
        + "années, et il n'y en a aucune à parcourir.");

    /// <summary>
    /// An attribution pass that could place <b>nobody at all</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>One unplaceable student is a fact; ten thousand is a broken catalogue, and the two must
    /// not read the same.</b> A text with no <c>AppliesToEntrantsFrom</c> is not malformed — it is
    /// <i>citation-only</i>, which arrêté 2175.22 legitimately is — so a base where <em>every</em>
    /// text is in that state throws nothing, refuses nothing, and simply selects no candidate for
    /// anyone. Measured on the 2026-09-01 rebuild: 10 185 of 10 185 students unresolved, 0 stamped,
    /// reported as a count by a pass that returned success. The cause is that
    /// <c>CnpnVersioning</c> reads its intake years out of <c>AcademicYears</c>, which is empty when
    /// the migration chain runs before the import — see <c>CnpnIntakeYearsBackfill</c>.
    /// </remarks>
    public static Error NoTextGovernsAnyIntake(int studentsConsidered) => Error.Problem(
        "Cnpn.NoTextGovernsAnyIntake",
        $"Aucun des {studentsConsidered} étudiants n'a pu être rattaché à un CNPN : aucun texte "
        + "enregistré ne revendique d'année d'entrée. Un texte sans « année d'entrée » n'est pas "
        + "invalide — il est conservé pour citation et n'est jamais sélectionné — donc rien ne "
        + "signale l'anomalie de lui-même. Vérifiez « AppliesToEntrantsFrom » sur les textes avant "
        + "de relancer.");

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
    /// <see cref="CnpnAssignment"/> picks the latest intake at or before a student's entry, and a tie has no
    /// defensible winner.
    /// </summary>
    public static Error IntakeYearAlreadyTaken(AcademicProgram program, string otherCode) => Error.Conflict(
        "Cnpn.IntakeYearAlreadyTaken",
        $"Le CNPN {otherCode} régit déjà les entrants de cette année en {program} ; deux textes ne "
        + "peuvent pas se disputer une même promotion.");

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

    /// <summary>
    /// The same gate, from the other side. A text stamped on a registration is the record of what a
    /// student was required to do that year; deleting it would leave closed years pointing at nothing.
    /// </summary>
    public static Error CannotDeleteWithRegistrations(string code, int registrationCount) => Error.Conflict(
        "Cnpn.CannotDeleteWithRegistrations",
        $"{registrationCount} inscription(s) ont été régies par le CNPN {code}. Supprimer le texte "
        + "effacerait ce que ces années exigeaient — rattachez-les à un autre texte d'abord.");

    // === Effectivity — « ce texte régit tel niveau à partir de telle année » ===

    public static Error EffectivityNotFound(int id) => Error.NotFound(
        "CnpnEffectivity.NotFound",
        $"Aucune règle d'entrée en vigueur enregistrée sous l'identifiant {id}.");

    /// <summary>
    /// Two texts starting to govern one level in one year. Resolution takes the latest start date at
    /// or before the registration's year, so a tie has no defensible winner — the same objection as
    /// two texts claiming one intake.
    /// </summary>
    public static Error EffectivityYearAlreadyTaken(string levelLabel, string yearLabel, string otherCode) =>
        Error.Conflict(
            "CnpnEffectivity.YearAlreadyTaken",
            $"Le CNPN {otherCode} prend déjà effet pour {levelLabel} en {yearLabel} ; deux textes ne "
            + "peuvent pas entrer en vigueur au même moment pour un même niveau.");

    /// <summary>
    /// The population moved between the preview and the apply. Same guard as the déliberation's
    /// <c>DefaultsNotConfirmed</c>, and for the same reason: a registration created in between widens
    /// the act silently, and a tick-box confirmation cannot notice.
    /// </summary>
    public static Error EffectivityMoveCountNotConfirmed(int confirmed, int actual) => Error.Conflict(
        "CnpnEffectivity.MoveCountNotConfirmed",
        $"Vous avez confirmé {confirmed} inscription(s) à re-rattacher, mais {actual} le seraient "
        + "maintenant. Relancez l'aperçu avant d'appliquer.");

    public static readonly Error EffectivityNothingToApply = Error.Problem(
        "CnpnEffectivity.NothingToApply",
        "Aucune inscription à re-rattacher : toutes relèvent déjà de ce texte, ou aucune n'existe "
        + "encore pour ce niveau depuis l'entrée en vigueur.");
}
