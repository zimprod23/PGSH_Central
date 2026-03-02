using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Create;

public sealed class CreateCenterCommandValidator : AbstractValidator<CreateCenterCommand>
{
    public CreateCenterCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(200);

        RuleFor(x => x.City)
            .MaximumLength(100);

        RuleFor(x => x.CenterType)
            .IsInEnum()
            .NotEqual(CenterType.None)
            .WithMessage("A valid Center Type must be selected.");

        // Optional: Validate coordinate formats if they aren't null
        RuleFor(x => x.LocalizationX).MaximumLength(50);
        RuleFor(x => x.LocalizationY).MaximumLength(50);
    }
}