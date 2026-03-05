using FluentValidation;

namespace PGSH.Application.Stages.Cohorts.Create;

public sealed class CreateCohortCommandValidator : AbstractValidator<CreateCohortCommand>
{
    public CreateCohortCommandValidator()
    {
        RuleFor(x => x.StageId)
            .NotEmpty().WithMessage("The Stage ID is required.");

        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("A label for the cohort is required (e.g., 'Promotion 2025/2026').")
            .MaximumLength(100);
    }
}