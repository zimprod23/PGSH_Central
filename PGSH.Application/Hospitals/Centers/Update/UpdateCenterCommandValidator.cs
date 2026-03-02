using FluentValidation;
using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Centers.Update;

public sealed class UpdateCenterCommandValidator : AbstractValidator<UpdateCenterCommand>
{
    public UpdateCenterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CenterType).IsInEnum().NotEqual(CenterType.None);
        RuleFor(x => x.City).MaximumLength(100);
    }
}