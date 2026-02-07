using FluentValidation;

namespace PGSH.Application.Stages.Levels.Update;

public sealed class UpdateLevelCommandValidator : AbstractValidator<UpdateLevelCommand>
{
    public UpdateLevelCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Year).InclusiveBetween(1, 10);
        RuleFor(x => x.AcademicProgram).IsInEnum();
    }
}