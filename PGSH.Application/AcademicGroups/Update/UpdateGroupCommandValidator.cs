using FluentValidation;

namespace PGSH.Application.AcademicGroups.Update;

public sealed class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
    }
}
