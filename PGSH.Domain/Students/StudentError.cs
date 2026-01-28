
using PGSH.SharedKernel;

namespace PGSH.Domain.Students;
public static class StudentErrors
{
    public static Error NotFound(Guid studentId) => Error.NotFound(
        "Students.NotFound",
        $"The student with Id = '{studentId}' was not found.");

    public static Error NotFoundByCNE(string cne) => Error.NotFound(
        "Students.NotFoundByCNE",
        $"The student with CNE = '{cne}' was not found.");

    public static Error DuplicateCNE(string cne) => Error.Conflict(
        "Students.DuplicateCNE",
        $"A student with CNE/CIN = '{cne}' already exists.");

    public static Error Conflict(string field, string value) => Error.Conflict(
        "Students.Conflict",
        $"A student with the {field} '{value}' already exists.");

    public static readonly Error InvalidBacYear = Error.Validation(
        "Students.InvalidBacYear",
        "The provided Baccalaureate year is invalid or missing.");

    public static readonly Error InvalidAccessGrade = Error.Validation(
        "Students.InvalidAccessGrade",
        "The access grade must be greater than or equal to 10.00.");

    public static readonly Error MissingSpecialty = Error.Validation(
        "Students.MissingSpecialty",
        "A specialty (academic program) must be assigned to the student.");

    public static readonly Error InvalidAgreementType = Error.Validation(
        "Students.InvalidAgreementType",
        "The provided agreement type is not valid for this context.");

    public static readonly Error InvalidBacSeries = Error.Validation(
        "Students.InvalidBacSeries",
        "The provided Baccalaureate series is invalid or not supported.");

    public static readonly Error MissingAppogee = Error.Validation(
        "Students.MissingAppogee",
        "The student's Appogee number is required and cannot be empty.");
}