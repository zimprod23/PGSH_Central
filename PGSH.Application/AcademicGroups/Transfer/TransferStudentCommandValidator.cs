using FluentValidation;
using PGSH.Domain.Registrations;

namespace PGSH.Application.AcademicGroups.Transfer;

public sealed class TransferStudentCommandValidator : AbstractValidator<TransferStudentCommand>
{
    public TransferStudentCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.TargetGroupId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A transfer reason is required for traceability.");
        RuleFor(x => x.Type).IsInEnum();

        // A temporary transfer applies to one stage only, so the caller must say which one.
        RuleFor(x => x.StageId)
            .NotNull().GreaterThan(0)
            .When(x => x.Type == TransferType.Temporary)
            .WithMessage("A temporary transfer must target a specific stage.");
    }
}
