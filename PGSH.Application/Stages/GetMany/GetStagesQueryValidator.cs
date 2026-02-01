using FluentValidation;

namespace PGSH.Application.Stages.GetMany;

internal class GetStagesQueryValidator : AbstractValidator<GetStagesQuery>
{
    public GetStagesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page number must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");

        // Optional: Limit SearchTerm length to avoid heavy string processing
        RuleFor(x => x.SearchTerm)
            .MaximumLength(50)
            .When(x => !string.IsNullOrEmpty(x.SearchTerm));
    }
}
