using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Update;

public sealed class UpdateHospitalCommandValidator : AbstractValidator<UpdateHospitalCommand>
{
    public UpdateHospitalCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CenterId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.HospitalType).IsInEnum().NotEqual(HospitalType.None);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}
