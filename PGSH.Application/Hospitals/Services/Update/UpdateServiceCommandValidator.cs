using FluentValidation;

namespace PGSH.Application.Hospitals.Services.Update;

public sealed class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 200);
        RuleFor(x => x.HospitalId).NotEmpty();
    }
}