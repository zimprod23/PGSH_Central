using FluentValidation;
using PGSH.Domain.Users;

namespace PGSH.Application.Students.Create;

/// <summary>
/// What creating a student from the admin form must satisfy.
/// </summary>
/// <remarks>
/// ⚠ <b>Deliberately stricter than <c>UpdateStudentCommandValidator</c>, on two rules only</b> — a
/// gender that is not <c>None</c>, and a date of birth. Nothing already stored is judged by this
/// validator, so demanding them here costs nobody a read-only record; a human at a form can be asked,
/// and refusing at creation is the cheap end of the rule this file's siblings exist about. The bulk
/// paths (<c>InscriptionPlanner</c>, <c>PGSH.LegacyImport</c>) do not come through here and must go on
/// storing <c>None</c> and <c>null</c> when the source says nothing — inventing either would be a
/// guess written into a student's file.
/// </remarks>
public sealed class CreateStudentCommandValidator : AbstractValidator<CreateStudentCommand>
{
    public CreateStudentCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .UniversityEmail();

        RuleFor(x => x.CNE).ValidCne();

        // ⚠ Not `int.TryParse`. A validator describes what a *save* must satisfy, so a rule the
        // stored data does not meet makes those rows read-only — and the refusal names a field
        // nobody was editing. `InscriptionPlanner` derives « SANS-APOGEE-<cne> » for a student whose
        // numéro Apogée the faculty has not allocated yet, which is not a number and never will be.
        // Same mistake as the old CNE regex (5 646 unsaveable students) and `Objectives.NotEmpty()`
        // (the whole stage catalogue). Length and presence are what the column actually requires.
        RuleFor(x => x.Appogee)
            .NotEmpty()
            .MaximumLength(StudentIdentifierRules.MaxAppogeeLength);

        RuleFor(x => x.Gender)
            .IsInEnum()
            .NotEqual(Gender.None);

        RuleFor(x => x.AcademicProgram).IsInEnum();
        RuleFor(x => x.BacSeries).IsInEnum();
        RuleFor(x => x.CivilStatus).IsInEnum();
        RuleFor(x => x.NationalityStatus).IsInEnum();

        // ⚠ The column's width, not a rounder number: capped at 50 here and truncated to 100 by the
        // import, a name of 51 to 100 characters could be imported and then never saved again.
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(StudentIdentifierRules.MaxNameLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(StudentIdentifierRules.MaxNameLength);

        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .Must(date => date < DateOnly.FromDateTime(DateTime.Now.AddYears(-15)))
            .WithMessage("Student must be at least 15 years old.");
    }
}