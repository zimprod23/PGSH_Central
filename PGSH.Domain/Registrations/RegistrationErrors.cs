using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public static class RegistrationErrors
{
    // === Registration ===
    public static Error NotFound(Guid registrationId) => Error.NotFound(
        "Registrations.NotFound",
        $"The registration with Id = '{registrationId}' was not found.");

    public static Error DuplicateRegistration(Guid studentId, int academicYear) => Error.Conflict(
        "Registrations.Duplicate",
        $"A registration for student '{studentId}' already exists for the academic year '{academicYear}'.");

    public static Error Conflict(string Action, Guid Id) => Error.Validation(
       "Registrations.Conflict",
       $"Somthing Went wrong while trying the '{Action}' Action on the registration with the Id '{Id}'");

    public static readonly Error MissingStudentReference = Error.Validation(
        "Registrations.MissingStudentReference",
        "Each registration must be linked to a valid student.");

    public static readonly Error MissingAcademicYear = Error.Validation(
        "Registrations.MissingAcademicYear",
        "A valid academic year must be provided for the registration.");

    public static readonly Error InvalidStatus = Error.Validation(
        "Registrations.InvalidStatus",
        "The registration status is invalid or not recognized.");

    public static readonly Error MissingLevel = Error.Validation(
        "Registrations.MissingLevel",
        "Each registration must have a valid academic level.");

    public static readonly Error ProgramMismatch = Error.Validation(
        "Registrations.ProgramMismatch",
        "The selected level does not belong to the student's academic program.");

    public static readonly Error ChronologicalInconsistency = Error.Validation(
        "Registrations.ChronologicalInconsistency",
        "The registration year and level are inconsistent with the student's existing academic progression.");

    public static readonly Error GroupingNotAllowed = Error.Forbidden(
        "Registrations.GroupingNotAllowed",
        "Seule la scolarité peut affecter un étudiant à un groupe.");

    // === Year outcome (déliberation) ===
    public static readonly Error OutcomeNotAllowed = Error.Forbidden(
        "Registrations.OutcomeNotAllowed",
        "Seule la scolarité peut enregistrer la décision d'une année.");

    /// <summary>
    /// « Diplômé » on a year that is not the last of the student's own text. Same rule as the
    /// déliberation canvas applies row by row, and it stands aside the same way where no text is
    /// recorded — one student at a time must not be stricter than five hundred at once.
    /// </summary>
    public static Error NotAFinalYear(int levelYear, int totalYears) => Error.Validation(
        "Registrations.NotAFinalYear",
        $"« Diplômé » sur une {levelYear}ᵉ année alors que le CNPN de cet étudiant en compte {totalYears}.");

    /// <summary>
    /// Re-opening a year that was never closed. Not an error worth much ceremony, but returning
    /// success would tell the caller a verdict was withdrawn when there was none.
    /// </summary>
    public static readonly Error NoOutcomeToReopen = Error.Conflict(
        "Registrations.NoOutcomeToReopen",
        "Cette inscription ne porte aucune décision d'année — il n'y a rien à rouvrir.");

    public static Error NotAYearOutcome(RegistrationStatus status) => Error.Validation(
        "Registrations.NotAYearOutcome",
        $"'{status}' is not a verdict a deliberation can pronounce — it is a position in a year that is still running.");

    public static Error OutcomeAlreadyDeclared(Guid registrationId) => Error.Conflict(
        "Registrations.OutcomeAlreadyDeclared",
        $"The registration '{registrationId}' already carries a verdict declared by the faculty; an inferred one cannot replace it.");

    // === Governing CNPN ===

    /// <summary>
    /// Re-stamping the CNPN of a year that has already been pronounced. The verdict was recorded
    /// against a requirement set; moving that set afterwards makes the verdict unreadable — nobody
    /// can tell what the jury was ruling on. Re-open the year first if the stamp is genuinely wrong.
    /// </summary>
    public static Error CnpnFrozenByOutcome(Guid registrationId) => Error.Conflict(
        "Registrations.CnpnFrozenByOutcome",
        $"L'inscription '{registrationId}' porte déjà une décision d'année : son CNPN ne peut plus "
        + "changer. Rouvrez l'année d'abord si le rattachement est erroné.");

    // === Entering the final year ===

    /// <summary>
    /// The last year of a cursus cannot begin while a stage from an earlier one is still unvalidated.
    /// Waivable — <c>FinalYearEntryWaiver</c> — because the faculty does grant exceptions, and one it
    /// cannot record is one that gets granted in SQL instead.
    /// </summary>
    public static Error FinalYearBlocked(int levelYear, int outstanding, string stages) => Error.Conflict(
        "Registrations.FinalYearBlocked",
        $"La {levelYear}ᵉ année est la dernière de ce cursus et ne peut pas être entamée : "
        + $"{outstanding} stage(s) antérieur(s) ne sont pas validés — {stages}. "
        + "Faites-les revalider, ou accordez une dérogation nominative.");

    public static readonly Error WaiverReasonRequired = Error.Validation(
        "FinalYearWaiver.ReasonRequired",
        "Une dérogation doit être motivée : indiquez qui l'accorde et pourquoi.");

    public static Error WaiverAlreadyGranted(Guid studentId, string yearLabel) => Error.Conflict(
        "FinalYearWaiver.AlreadyGranted",
        $"Une dérogation existe déjà pour cet étudiant en {yearLabel}.");

    public static Error WaiverNotFound(Guid id) => Error.NotFound(
        "FinalYearWaiver.NotFound",
        $"Aucune dérogation enregistrée sous l'identifiant '{id}'.");

    /// <summary>
    /// The registration the waiver permitted already exists, so the waiver is now its justification.
    /// Removing it would leave a student sitting in a final year with an unvalidated stage and nothing
    /// on record saying who allowed it — the exact state the waiver exists to prevent.
    /// </summary>
    public static Error WaiverAlreadyUsed(Guid id) => Error.Conflict(
        "FinalYearWaiver.AlreadyUsed",
        $"La dérogation '{id}' a déjà servi : l'inscription qu'elle autorise existe. La retirer "
        + "laisserait cette année sans justification.");

    /// <summary>
    /// Granting a waiver to a student who owes nothing. Not harmful, but it would sit in the record
    /// as evidence of an exception that never happened.
    /// </summary>
    public static readonly Error WaiverNotNeeded = Error.Problem(
        "FinalYearWaiver.NotNeeded",
        "Cet étudiant ne doit aucun stage antérieur : aucune dérogation n'est nécessaire.");

    // === FailureReasons ===
    public static Error FailureReasonNotFound(Guid registrationId) => Error.NotFound(
        "FailureReasons.NotFound",
        $"No failure reason found for registration Id = '{registrationId}'.");

    public static readonly Error InvalidFailureDescription = Error.Validation(
        "FailureReasons.InvalidDescription",
        "The failure reason description cannot be null or empty.");

    public static readonly Error InvalidFailureNotes = Error.Validation(
        "FailureReasons.InvalidNotes",
        "Failure notes must contain at least one valid entry.");

    public static readonly Error CheatDetected = Error.Validation(
        "FailureReasons.CheatDetected",
        "A cheating incident was detected for this registration.");
}
