using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears;

/// <summary>
/// Turns "the year the caller meant" into an id, for every handler whose result is year-scoped.
///
/// The rule it enforces is that there is always exactly one: an omitted year falls back to the
/// current one and never to "all of them". Widening on absence is what made a stage query return the
/// 681 cohorts of every year it ever ran, and the import canvas list 3,500 students across seven
/// promotions. A read that spans years has to say so explicitly, by other means than leaving the
/// filter out.
/// </summary>
internal sealed class AcademicYearResolver(IApplicationDbContext dbContext)
{
    /// <summary><paramref name="requested"/> if given, otherwise the year flagged current.</summary>
    public async Task<Result<int>> ResolveAsync(int? requested, CancellationToken ct)
    {
        if (requested is { } id)
        {
            bool exists = await dbContext.AcademicYears.AnyAsync(y => y.Id == id, ct);
            return exists
                ? Result.Success(id)
                : Result.Failure<int>(StageErrors.AcademicYearNotFound(id));
        }

        int currentId = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.IsCurrent)
            .Select(y => y.Id)
            .FirstOrDefaultAsync(ct);

        return currentId == 0
            ? Result.Failure<int>(StageErrors.NoCurrentAcademicYear)
            : Result.Success(currentId);
    }

    /// <summary>The resolved year together with its label, for messages that name it.</summary>
    public async Task<Result<(int Id, string Label)>> ResolveWithLabelAsync(int? requested, CancellationToken ct)
    {
        var resolved = await ResolveAsync(requested, ct);
        if (resolved.IsFailure)
            return Result.Failure<(int, string)>(resolved.Error);

        string label = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == resolved.Value)
            .Select(y => y.Label)
            .FirstAsync(ct);

        return Result.Success((resolved.Value, label));
    }
}
