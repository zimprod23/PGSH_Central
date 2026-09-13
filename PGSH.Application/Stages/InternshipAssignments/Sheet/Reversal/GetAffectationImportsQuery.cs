using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Extensions;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

/// <summary>
/// The imports applied to one promotion, newest first — the list « laquelle veux-tu annuler ? » is
/// chosen from.
/// </summary>
/// <remarks>
/// ⚠ <b>Reversed imports are listed too, and that is deliberate.</b> Hiding them would answer « il ne
/// s'est rien passé » to somebody asking why a promotion's plan changed twice. The row says which it
/// is, and <c>CanBeReversed</c> says whether the button is worth offering.
///
/// <para>⚠ Scoped to a promotion, and <paramref name="AcademicYearId"/> omitted means the current year
/// — never all of them.</para>
/// </remarks>
public sealed record GetAffectationImportsQuery(
    int? LevelId = null,
    int? AcademicYearId = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResponse<AffectationImportSummary>>;

internal sealed class GetAffectationImportsQueryValidator
    : AbstractValidator<GetAffectationImportsQuery>
{
    public GetAffectationImportsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsAPageNumber();
        RuleFor(x => x.PageSize).IsAPageSize();
    }
}

internal sealed class GetAffectationImportsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetAffectationImportsQuery, PaginatedResponse<AffectationImportSummary>>
{
    public async Task<Result<PaginatedResponse<AffectationImportSummary>>> Handle(
        GetAffectationImportsQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<PaginatedResponse<AffectationImportSummary>>(access.Error);

        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PaginatedResponse<AffectationImportSummary>>(year.Error);

        return await ImportsQuery(dbContext, year.Value, request.LevelId)
            .AsNoTracking()
            .OrderByDescending(i => i.AppliedAtUtc)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                i => new AffectationImportSummary(
                    i.Id,
                    i.AcademicYear.Label,
                    dbContext.Levels.Where(l => l.Id == i.LevelId).Select(l => l.Label).FirstOrDefault()
                        ?? $"Niveau {i.LevelId}",
                    i.FileName,
                    i.AppliedAtUtc,
                    dbContext.Users.Where(u => u.Id == i.AppliedByUserId)
                        .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault(),
                    i.Status,
                    i.ReversedAtUtc,
                    i.AffectationCount,
                    i.ReplacedPeriodCount,
                    i.Status == AffectationImportStatus.Applied),
                cancellationToken);
    }

    internal static IQueryable<AffectationImport> ImportsQuery(
        IApplicationDbContext dbContext, int academicYearId, int? levelId) =>
        dbContext.AffectationImports
            .Where(i => i.AcademicYearId == academicYearId
                     && (levelId == null || i.LevelId == levelId));
}
