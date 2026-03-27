using FluentValidation;

namespace PGSH.Application.Students.Registrations.CreateMany;

public sealed class CreateManyRegistrationsCommandValidator : AbstractValidator<CreateManyRegistrationsCommand>
{
    public CreateManyRegistrationsCommandValidator()
    {
        RuleFor(x => x.StudentIds).NotEmpty().WithMessage("Student list cannot be empty.");
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.AcademicYearId).NotEmpty();
        RuleFor(x => x.Status).NotEmpty().MaximumLength(50);
    }
}