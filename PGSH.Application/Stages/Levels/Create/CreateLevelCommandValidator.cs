using FluentValidation;

namespace PGSH.Application.Stages.Levels.Create;

public sealed class CreateLevelCommandValidator : AbstractValidator<CreateLevelCommand>
{
    public CreateLevelCommandValidator()
    {
        RuleFor(x => x.Label)
            .NotEmpty().MaximumLength(100);

        RuleFor(x => x.Year)
            .InclusiveBetween(1, 10); // Adjust based on your curriculum length

        RuleFor(x => x.AcademicProgram)
            .IsInEnum(); // Ensures the int maps to a valid AcademicProgram enum
    }
}
