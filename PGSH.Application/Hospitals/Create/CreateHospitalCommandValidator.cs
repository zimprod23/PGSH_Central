using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Create;

public sealed class CreateHospitalCommandValidator : AbstractValidator<CreateHospitalCommand>
{
    public CreateHospitalCommandValidator()
    {
        RuleFor(x => x.CenterId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(HospitalTextLengths.Name);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(HospitalTextLengths.City);

        // ⚠ Bounded to the column, not left to the database to refuse in a language nobody reads.
        RuleFor(x => x.Description).MaximumLength(HospitalTextLengths.Description);
        RuleFor(x => x.Email).MaximumLength(HospitalTextLengths.Email);
        RuleFor(x => x.LocalizationX).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationY).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationZ).MaximumLength(HospitalTextLengths.Coordinate);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.HospitalType)
            .IsInEnum()
            .NotEqual(HospitalType.None);
    }
}
