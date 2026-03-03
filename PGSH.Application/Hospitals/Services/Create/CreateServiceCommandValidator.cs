using FluentValidation;

namespace PGSH.Application.Hospitals.Services.Create;

public sealed class CreateServiceCommandValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator()
    {
        RuleFor(x => x.HospitalId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Capacity).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.ServiceType).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(500);
    }
}