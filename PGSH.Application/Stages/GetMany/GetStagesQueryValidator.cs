using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.Stages.GetMany;

internal class GetStagesQueryValidator : AbstractValidator<GetStagesQuery>
{
    public GetStagesQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsAPageNumber();

        // ⚠ Was a hand-written ceiling of 100, stricter than the one the pipeline actually enforces.
        // The CNPN editor asks for a level's whole catalogue in one page and was refused outright —
        // see PaginationRules.
        RuleFor(x => x.PageSize).IsAPageSize();

        RuleFor(x => x.SearchTerm)
            .MaximumLength(50)
            .When(x => !string.IsNullOrEmpty(x.SearchTerm));
    }
}
