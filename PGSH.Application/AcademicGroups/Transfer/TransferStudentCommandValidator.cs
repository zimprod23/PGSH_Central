using FluentValidation;

namespace PGSH.Application.AcademicGroups.Transfer;

public sealed class TransferStudentCommandValidator : AbstractValidator<TransferStudentCommand>
{
    public TransferStudentCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.TargetGroupId).GreaterThan(0);
    }
}
