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
            .MaximumLength(200);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.HospitalType)
            .IsInEnum()
            .NotEqual(HospitalType.None);
    }
}