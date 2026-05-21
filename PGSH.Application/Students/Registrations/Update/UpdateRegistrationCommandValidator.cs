using FluentValidation;

namespace PGSH.Application.Students.Registrations.Update;

public sealed class UpdateRegistrationCommandValidator : AbstractValidator<UpdateRegistrationCommand>
{
    public UpdateRegistrationCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.FailureDescription).MaximumLength(500);
    }
}