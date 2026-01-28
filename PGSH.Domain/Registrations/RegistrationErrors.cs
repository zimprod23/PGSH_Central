using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public static class RegistrationErrors
{
    // === Registration ===
    public static Error NotFound(Guid registrationId) => Error.NotFound(
        "Registrations.NotFound",
        $"The registration with Id = '{registrationId}' was not found.");

    public static Error DuplicateRegistration(Guid studentId, DateOnly academicYear) => Error.Conflict(
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
