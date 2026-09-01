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
        // ⚠ **No `NotEmpty()` here.** It described the stage somebody would *author* and was applied
        // to every save, so the 27 imported stages — objectives are pedagogical detail the Access base
        // never carried, and **0 of 27** have one — could not be edited at all, whatever field was
        // being changed. Flipping `RotationMode` was refused with "At least one stage objective is
        // required", which names a field the form was not touching. Same mistake as the CNE regex that
        // made 5,646 students unsaveable; here it was the whole catalogue.
        //
        // Objectives are genuinely optional: only `EvaluationMode.ValidateObjectives` needs any, and
        // that is enforced where it is true — `CreateServiceEvaluationCommandValidator` and
        // `EvaluationObjectiveResolver`. The per-objective rules below still validate what *is* sent.
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