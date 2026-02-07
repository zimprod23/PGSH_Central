using FluentValidation;

namespace PGSH.Application.Stages.Levels.GetMany;

public sealed class GetLevelsQueryValidator : AbstractValidator<GetLevelsQuery>
{
    public GetLevelsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}