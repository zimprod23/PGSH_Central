using FluentValidation;
using PGSH.Application.Stages.Evaluations;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Delocalization;

public sealed class DelocalizeStudentCommandValidator : AbstractValidator<DelocalizeStudentCommand>
{
    public DelocalizeStudentCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Un motif est requis pour la délocalisation.");

        // Only when both are supplied. Either one alone is meaningless, and neither means « take the
        // stage's own window » — which is the ordinary case, not an omission to be corrected.
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");

        RuleFor(x => x.StartDate)
            .NotNull()
            .When(x => x.EndDate is not null)
            .WithMessage("Indiquez la date de début, ou laissez les deux dates vides.");

        RuleFor(x => x.EndDate)
            .NotNull()
            .When(x => x.StartDate is not null)
            .WithMessage("Indiquez la date de fin, ou laissez les deux dates vides.");

        RuleFor(x => x.Verdict!)
            .SetValidator(new DelocalizationVerdictValidator())
            .When(x => x.Verdict is not null);
    }
}

/// <summary>
/// The paper verdict, checked in whichever form it arrived.
/// </summary>
/// <remarks>
/// ⚠ Deliberately the same rules as <c>CreateServiceEvaluationCommandValidator</c>, because the two
/// write the same <see cref="ServiceEvaluation"/>: a mode accepted here and refused there would make
/// a mark enterable by délocalisation and uncorrectable afterwards.
/// </remarks>
public sealed class DelocalizationVerdictValidator : AbstractValidator<DelocalizationVerdict>
{
    public DelocalizationVerdictValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.FicheReference).MaximumLength(1000);

        When(x => x.Mode == EvaluationMode.Numeric, () =>
        {
            RuleFor(x => x.TotalScore).NotNull().InclusiveBetween(0, 20);
            RuleForEach(x => x.ObjectiveScores!).ChildRules(o =>
            {
                o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
                o.RuleFor(s => s.Score).NotNull().InclusiveBetween(0, 20);
            }).When(x => x.ObjectiveScores is not null);
        });

        When(x => x.Mode == EvaluationMode.ValidatePeriod, () =>
        {
            RuleFor(x => x.Outcome).NotNull().IsInEnum();
        });

        When(x => x.Mode == EvaluationMode.ValidateObjectives, () =>
        {
            RuleFor(x => x.ObjectiveScores).NotNull().NotEmpty();
            RuleForEach(x => x.ObjectiveScores!).ChildRules(o =>
            {
                o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
                o.RuleFor(s => s.Outcome).NotNull().IsInEnum();
            }).When(x => x.ObjectiveScores is not null);
        });
    }
}
