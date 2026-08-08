using FluentValidation;

namespace PGSH.Application.Stages.Revalidation;

public sealed class RevalidateStageCommandValidator : AbstractValidator<RevalidateStageCommand>
{
    public RevalidateStageCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.CohortId).GreaterThan(0).When(x => x.CohortId is not null);
        RuleFor(x => x.ServiceId).GreaterThan(0).When(x => x.ServiceId is not null);
        RuleFor(x => x.Reason).MaximumLength(500);

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");
    }
}
