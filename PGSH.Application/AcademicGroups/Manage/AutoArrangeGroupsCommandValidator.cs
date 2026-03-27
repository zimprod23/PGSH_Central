using FluentValidation;

namespace PGSH.Application.AcademicGroups.Manage;

public sealed class AutoArrangeGroupsCommandValidator : AbstractValidator<AutoArrangeGroupsCommand>
{
    public AutoArrangeGroupsCommandValidator()
    {
        RuleFor(x => x.LevelId)
            .NotEmpty()
            .WithMessage("Level is required for group distribution.");

        RuleFor(x => x.AcademicYearId)
            .NotEmpty()
            .WithMessage("Academic Year is required.");

        RuleFor(x => x.GroupSize)
            .GreaterThan(0)
            .WithMessage("Group size must be at least 1 student.")
            .LessThanOrEqualTo(100)
            .WithMessage("Group size seems too high for standard rotations. Please verify.");
    }
}