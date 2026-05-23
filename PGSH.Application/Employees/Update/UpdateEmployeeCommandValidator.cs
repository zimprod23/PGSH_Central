using FluentValidation;

namespace PGSH.Application.Employees.Update;

internal sealed class UpdateEmployeeCommandValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Grade).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.WorkPlace).IsInEnum().When(x => x.WorkPlace.HasValue);
        RuleFor(x => x.PPR).MaximumLength(50).When(x => x.PPR != null);
        RuleFor(x => x.Label).MaximumLength(100).When(x => x.Label != null);
        RuleFor(x => x.CIN).MaximumLength(20).When(x => x.CIN != null);
    }
}
