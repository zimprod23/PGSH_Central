using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.Stages.Levels.GetMany;

public sealed class GetLevelsQueryValidator : AbstractValidator<GetLevelsQuery>
{
    public GetLevelsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsAPageNumber();
        RuleFor(x => x.PageSize).IsAPageSize();
    }
}
