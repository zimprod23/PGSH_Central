using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.Students.GetMany
{
    public sealed class GetStudentsQueryValidator : AbstractValidator<GetStudentsQuery>
    {
        public GetStudentsQueryValidator()
        {
            RuleFor(x => x.PageNumber).IsAPageNumber();
            RuleFor(x => x.PageSize).IsAPageSize();

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
