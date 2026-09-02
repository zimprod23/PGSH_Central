using FluentValidation;
using PGSH.Domain.Users;

namespace PGSH.Application.Students.Create;

public sealed class CreateStudentCommandValidator : AbstractValidator<CreateStudentCommand>
{
    public CreateStudentCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .Must(email => email.EndsWith("@um5.ac.ma"))
            .WithMessage("Email must belong to the University domain (@um5.ac.ma).");

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

        // 4. Enums: Ensure they aren't "None" or out of range
        // This prevents "Alien" or "None" from being used if you want to restrict them
        RuleFor(x => x.Gender)
            .IsInEnum()
            .NotEqual(Gender.None);

        RuleFor(x => x.AcademicProgram).IsInEnum();
        RuleFor(x => x.BacSeries).IsInEnum();
        RuleFor(x => x.CivilStatus).IsInEnum();
        RuleFor(x => x.NationalityStatus).IsInEnum();

        // 5. Required Profile Fields
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .Must(date => date < DateOnly.FromDateTime(DateTime.Now.AddYears(-15)))
            .WithMessage("Student must be at least 15 years old.");
    }
}