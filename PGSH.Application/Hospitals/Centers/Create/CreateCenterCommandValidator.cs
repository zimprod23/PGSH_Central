using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Create;

public sealed class CreateCenterCommandValidator : AbstractValidator<CreateCenterCommand>
{
    public CreateCenterCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(HospitalTextLengths.Name);

        RuleFor(x => x.City)
            .MaximumLength(HospitalTextLengths.City);

        RuleFor(x => x.CenterType)
            .IsInEnum()
            .NotEqual(CenterType.None)
            .WithMessage("A valid Center Type must be selected.");

        // ⚠ Z was missing while X and Y were bounded — the same column, two of three checked.
        RuleFor(x => x.LocalizationX).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationY).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationZ).MaximumLength(HospitalTextLengths.Coordinate);
    }
}
