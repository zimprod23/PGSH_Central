using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears.Manage;

/// <summary>
/// The two rules a year cannot check alone, because both are about the <i>other</i> years: its label
/// is unique, and its span touches nobody else's.
/// </summary>
/// <remarks>
/// ⚠ <b>Non-overlap is load-bearing, not cosmetic.</b> <c>ServiceOccupancyCalculator</c> bounds a year
/// by its dates rather than by <c>AcademicYearId</c> — deliberately, so that a slot stamped with the
/// wrong year but dated inside this one still surfaces — and that choice is only safe while two years
/// cannot share a day. Overlapping years would count every slot in the overlap twice against a
/// service's load, which is the number the publish guard refuses on.
///
/// <para>Shared by create and update for the reason <c>StudentIdentifierRules</c> exists: a rule
/// enforced on one path only is a row that can be created and then never saved, or the reverse.</para>
/// </remarks>
public sealed class AcademicYearCalendarGuard(IApplicationDbContext dbContext)
{
    /// <param name="excludingId">The year being updated, which must not collide with itself.</param>
    public async Task<Result> EnsureFreeAsync(
        string label,
        DateOnly startDate,
        DateOnly endDate,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        if (endDate < startDate)
            return Result.Failure(AcademicYearErrors.EndsBeforeItStarts(startDate, endDate));

        string trimmed = label.Trim();

        var others = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => excludingId == null || y.Id != excludingId)
            .Select(y => new { y.Label, y.StartDate, y.EndDate })
            .ToListAsync(cancellationToken);

        if (others.Any(y => string.Equals(y.Label, trimmed, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure(AcademicYearErrors.DuplicateLabel(trimmed));

        // Inclusive on both ends: a year ending 31/08 and the next starting 31/08 share that day, and
        // a stage running on it would be counted in both.
        var clash = others.FirstOrDefault(y => y.StartDate <= endDate && startDate <= y.EndDate);

        return clash is null
            ? Result.Success()
            : Result.Failure(AcademicYearErrors.OverlapsAnotherYear(trimmed, clash.Label));
    }
}
