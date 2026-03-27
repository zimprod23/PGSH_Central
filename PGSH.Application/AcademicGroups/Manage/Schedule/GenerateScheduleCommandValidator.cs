using FluentValidation;

namespace PGSH.Application.AcademicGroups.Manage.Schedule;

public sealed class GenerateScheduleCommandValidator : AbstractValidator<GenerateScheduleCommand>
{
    public GenerateScheduleCommandValidator()
    {
        RuleFor(x => x.AcademicYearId).NotEmpty();
        RuleFor(x => x.AvailableServiceIds).NotEmpty().WithMessage("At least one hospital service must be selected.");

        RuleForEach(x => x.Stages).ChildRules(stage => {
            stage.RuleFor(s => s.NumberOfRotations).GreaterThanOrEqualTo(0);
            stage.RuleFor(s => s.RotationDurationDays).GreaterThan(0);
            stage.RuleFor(s => s.GlobalStartDate).NotEmpty();
        });
    }
}