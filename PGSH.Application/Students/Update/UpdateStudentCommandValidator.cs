using FluentValidation;
using PGSH.Domain.Users;

namespace PGSH.Application.Students.Update;

public sealed class UpdateStudentCommandValidator : AbstractValidator<UpdateStudentCommand>
{
    public UpdateStudentCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .Must(email => email.EndsWith("@um5.ac.ma"))
            .WithMessage("Email must belong to the University domain (@um5.ac.ma).");

        RuleFor(x => x.CNE)
            .NotEmpty()
            .Matches(@"^[A-Z]\d{6,12}$")
            .WithMessage("CNE must start with a letter followed by 6 to 12 digits.");

        RuleFor(x => x.Appogee)
            .NotEmpty()
            .Must(x => int.TryParse(x, out _))
            .WithMessage("Appogee must be a valid numeric identifier.");

        RuleFor(x => x.Gender).IsInEnum().NotEqual(Gender.None);
        RuleFor(x => x.AcademicProgram).IsInEnum();
        RuleFor(x => x.BacSeries).IsInEnum();
        RuleFor(x => x.CivilStatus).IsInEnum();
        RuleFor(x => x.NationalityStatus).IsInEnum();

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);

        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .Must(date => date < DateOnly.FromDateTime(DateTime.Now.AddYears(-15)))
            .WithMessage("Student must be at least 15 years old.");
    }
}
