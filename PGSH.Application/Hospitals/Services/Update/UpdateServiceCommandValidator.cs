using FluentValidation;

namespace PGSH.Application.Hospitals.Services.Update;

public sealed class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalTextLengths.Name);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 200);
        RuleFor(x => x.HospitalId).NotEmpty();
        RuleFor(x => x.ServiceType).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(HospitalTextLengths.Description);
        RuleFor(x => x.Specialty).MaximumLength(HospitalTextLengths.Specialty);

        RuleFor(x => x.LocalizationX).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationY).MaximumLength(HospitalTextLengths.Coordinate);
        RuleFor(x => x.LocalizationZ).MaximumLength(HospitalTextLengths.Coordinate);

        RuleForEach(x => x.LevelCapacities).SetValidator(new ServiceLevelCapacityRequestValidator());
    }
}
