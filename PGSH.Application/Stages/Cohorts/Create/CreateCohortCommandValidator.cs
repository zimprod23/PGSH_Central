using FluentValidation;

namespace PGSH.Application.Stages.Cohorts.Create;

public sealed class CreateCohortCommandValidator : AbstractValidator<CreateCohortCommand>
{
    public CreateCohortCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.AcademicGroupId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
    }
}