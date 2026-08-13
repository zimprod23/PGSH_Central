using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar;

/// <summary>
/// Fills in the fixed national holidays falling inside an academic year — the half of the calendar that is
/// law rather than observation, and therefore the half nobody should be typing.
/// </summary>
/// <remarks>
/// Idempotent by (date, name), the same key the unique index uses, so re-running after adding a year adds
/// only what is new. It deliberately does <b>not</b> touch religious or academic entries: those are
/// authored, and a seeder that could overwrite them would be a seeder that loses a decree.
/// </remarks>
public sealed record SeedNationalHolidaysCommand(int? AcademicYearId = null)
    : ICommand<SeedNationalHolidaysResult>, IAuditableCommand
{
    public string AuditAction => "HOLIDAYS_NATIONAL_SEEDED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId?.ToString();
    public string? AuditMetadata => null;
}

public sealed record SeedNationalHolidaysResult(
    string AcademicYearLabel,
    int Created,
    int AlreadyPresent,
    IReadOnlyList<string> MissingReligious);

internal sealed class SeedNationalHolidaysCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : ICommandHandler<SeedNationalHolidaysCommand, SeedNationalHolidaysResult>
{
    public async Task<Result<SeedNationalHolidaysResult>> Handle(
        SeedNationalHolidaysCommand request, CancellationToken cancellationToken)
    {
        var resolved = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<SeedNationalHolidaysResult>(resolved.Error);

        var year = await dbContext.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == resolved.Value, cancellationToken);

        if (year is null)
            return Result.Failure<SeedNationalHolidaysResult>(
                Error.NotFound("AcademicYears.NotFound", $"Année {resolved.Value} introuvable."));

        // An academic year straddles two Gregorian ones (September → July), so both are generated and
        // then clipped to the year's own span.
        var candidates = Enumerable
            .Range(year.StartDate.Year, year.EndDate.Year - year.StartDate.Year + 1)
            .SelectMany(MoroccanPublicHolidays.FixedFor)
            .Where(h => h.StartDate >= year.StartDate && h.StartDate <= year.EndDate)
            .ToList();

        var existing = await dbContext.Holidays
            .Where(h => h.StartDate >= year.StartDate && h.StartDate <= year.EndDate)
            .Select(h => new { h.StartDate, h.Name })
            .ToListAsync(cancellationToken);

        var present = existing
            .Select(h => (h.StartDate, h.Name))
            .ToHashSet();

        var missing = candidates
            .Where(h => !present.Contains((h.StartDate, h.Name)))
            .ToList();

        if (missing.Count > 0)
        {
            await dbContext.Holidays.AddRangeAsync(missing, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var recorded = await dbContext.Holidays
            .Where(h => h.EndDate >= year.StartDate && h.StartDate <= year.EndDate)
            .Select(h => h.Name)
            .ToListAsync(cancellationToken);

        var recordedSet = recorded.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new SeedNationalHolidaysResult(
            year.Label,
            missing.Count,
            candidates.Count - missing.Count,
            MoroccanPublicHolidays.ExpectedReligious
                .Where(e => !recordedSet.Contains(e.Name))
                .Select(e => e.Name)
                .ToList());
    }
}
