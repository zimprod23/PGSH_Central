using FluentValidation;

namespace PGSH.Application.Stages.Create;

public sealed class CreateStageCommandValidator : AbstractValidator<CreateStageCommand>
{
    public CreateStageCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Stage name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Coefficient must be at least 1.");

        RuleFor(x => x.DurationInDays)
            .InclusiveBetween(1, 365).WithMessage("Duration must be between 1 day and 1 year.");

        RuleFor(x => x.LevelId)
            .NotEmpty().WithMessage("Level must be specified.");

        // Validation for the collection of Objectives
        RuleFor(x => x.Objectives)
            .NotEmpty().WithMessage("At least one stage objective is required.");
            //.Must(HaveValidTotalWeight).WithMessage("The total weight of objectives must sum up to 100.");

        // Rule for each individual objective item
        RuleForEach(x => x.Objectives).ChildRules(objective =>
        {
            objective.RuleFor(o => o.Label)
                .NotEmpty().WithMessage("Objective label is required.")
                .MaximumLength(200);

            objective.RuleFor(o => o.Weight)
                .GreaterThan(0).WithMessage("Weight must be greater than 0.");
        });
    }

    private bool HaveValidTotalWeight(List<CreateStageObjectiveRequest> objectives)
    {
        if (objectives == null || !objectives.Any()) return false;

        // Optimal: Ensure weight logic matches your grading system (usually 100%)
        return objectives.Sum(x => x.Weight) == 100;
    }
}