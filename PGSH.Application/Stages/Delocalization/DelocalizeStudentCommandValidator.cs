using FluentValidation;

namespace PGSH.Application.Stages.Delocalization;

public sealed class DelocalizeStudentCommandValidator : AbstractValidator<DelocalizeStudentCommand>
{
    public DelocalizeStudentCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Un motif est requis pour la délocalisation.");
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");

        RuleFor(x => x.Outcome).IsInEnum().When(x => x.Outcome is not null);
        RuleFor(x => x.FicheReference).MaximumLength(1000);
    }
}
