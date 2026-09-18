using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Update;

public sealed class UpdateCenterCommandValidator : AbstractValidator<UpdateCenterCommand>
{
    public UpdateCenterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalTextLengths.Name);
        RuleFor(x => x.CenterType).IsInEnum().NotEqual(CenterType.None);
        RuleFor(x => x.City).MaximumLength(HospitalTextLengths.City);

        RuleFor(x => x.LocalizationX).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationY).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationZ).MaximumLength(HospitalTextLengths.Coordinate);
    }
}
