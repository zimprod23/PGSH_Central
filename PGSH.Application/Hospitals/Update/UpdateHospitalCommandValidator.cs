using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Update;

public sealed class UpdateHospitalCommandValidator : AbstractValidator<UpdateHospitalCommand>
{
    public UpdateHospitalCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CenterId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalTextLengths.Name);
        RuleFor(x => x.City).NotEmpty().MaximumLength(HospitalTextLengths.City);
        RuleFor(x => x.HospitalType).IsInEnum().NotEqual(HospitalType.None);

        // ⚠ Every one of these was unbounded, against a column that is not. An over-long
        // description is refused here in words; unrefused it reached PostgreSQL, which answers
        // 22001 — a DbUpdateException, a 500, and a sentence the client discards.
        RuleFor(x => x.Description).MaximumLength(HospitalTextLengths.Description);
        RuleFor(x => x.Email).MaximumLength(HospitalTextLengths.Email);
        RuleFor(x => x.LocalizationX).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationY).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationZ).MaximumLength(HospitalTextLengths.Coordinate);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}
