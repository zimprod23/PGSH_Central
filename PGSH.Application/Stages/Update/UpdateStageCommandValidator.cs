using FluentValidation;

namespace PGSH.Application.Stages.Update;

public sealed class UpdateStageCommandValidator : AbstractValidator<UpdateStageCommand>
{
    public UpdateStageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Coefficient).GreaterThan(0);
        RuleFor(x => x.DurationInDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Objectives)
            .NotEmpty().WithMessage("At least one stage objective is required.");
        RuleForEach(x => x.Objectives).ChildRules(objective =>
        {
            objective.RuleFor(o => o.Label)
                .NotEmpty().WithMessage("Objective label is required.")
                .MaximumLength(200);

            objective.RuleFor(o => o.Weight)
                .GreaterThan(0).WithMessage("Weight must be greater than 0.");
        });
    }
}