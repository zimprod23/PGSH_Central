using FluentValidation;

namespace PGSH.Application.Students.GetMany
{
    public sealed class GetStudentsQueryValidator : AbstractValidator<GetStudentsQuery>
    {
        public GetStudentsQueryValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Page number must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("Page size must be between 1 and 100.");

            RuleFor(x => x.LevelId)
                .GreaterThan(0)
                .When(x => x.LevelId is not null)
                .WithMessage("Level id must be positive.");

            // Optional: Limit SearchTerm length to avoid heavy string processing
            RuleFor(x => x.SearchTerm)
                .MaximumLength(50)
                .When(x => !string.IsNullOrEmpty(x.SearchTerm));
        }
    }
}
