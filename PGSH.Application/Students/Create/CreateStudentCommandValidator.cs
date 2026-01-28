using FluentValidation;
using PGSH.Domain.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        // 2. CNE: Letter + 6 to 12 digits
        // We pre-compile the Regex for performance if this were a static utility, 
        // but FluentValidation handles its own caching well.
        RuleFor(x => x.CNE)
            .NotEmpty()
            .Matches(@"^[A-Z]\d{6,12}$")
            .WithMessage("CNE must start with a letter followed by 6 to 12 digits.");

        // 3. Appogee: Must be a numeric string (or number)
        RuleFor(x => x.Appogee)
            .NotEmpty()
            .Must(x => int.TryParse(x, out _))
            .WithMessage("Appogee must be a valid numeric identifier.");

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