using FluentValidation;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Evaluations.Update;

public sealed class UpdateServiceEvaluationCommandValidator : AbstractValidator<UpdateServiceEvaluationCommand>
{
    public UpdateServiceEvaluationCommandValidator()
    {
        RuleFor(x => x.EvaluationId).NotEmpty();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.ObjectiveScores).NotNull();

        When(x => x.Mode == EvaluationMode.Numeric, () =>
        {
            RuleFor(x => x.TotalScore).NotNull().InclusiveBetween(0, 20);
            RuleForEach(x => x.ObjectiveScores).ChildRules(o =>
            {
                o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
                o.RuleFor(s => s.Score).NotNull().InclusiveBetween(0, 20);
            });
        });

        When(x => x.Mode == EvaluationMode.ValidatePeriod, () =>
        {
            RuleFor(x => x.Outcome).NotNull().IsInEnum();
        });

        When(x => x.Mode == EvaluationMode.ValidateObjectives, () =>
        {
            RuleFor(x => x.ObjectiveScores).NotEmpty();
            RuleForEach(x => x.ObjectiveScores).ChildRules(o =>
            {
                o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
                o.RuleFor(s => s.Outcome).NotNull().IsInEnum();
            });
        });
    }
}
