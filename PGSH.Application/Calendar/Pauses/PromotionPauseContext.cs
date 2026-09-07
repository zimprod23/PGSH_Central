using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Application.Exports;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>The promotion a window belongs to: (année universitaire, niveau), and how to name it.</summary>
public sealed record Promotion(AcademicYear Year, Level Level, string LevelLabel);

/// <summary>
/// Turns « quelle promotion » into the two rows that decide a window, so the preview and the
/// declaration cannot disagree about which promotion they are talking about or what it is called.
/// </summary>
/// <remarks>
/// ⚠ <b>An omitted year is the current one, never all of them.</b> A window declared without a year
/// would otherwise be a window on a level, which is not a thing that exists: a promotion is the pair.
///
/// <para>The rows come back <b>tracked</b>, because <c>PromotionPause.Declare</c> attaches them as
/// navigations and a detached one would be inserted again. The read path takes the same rows for the
/// same labels rather than growing a second, no-tracking twin that could name a promotion differently.
/// </para>
/// </remarks>
internal sealed class PromotionPauseContext(IApplicationDbContext dbContext, AcademicYearResolver yearResolver)
{
    public async Task<Result<Promotion>> ResolveAsync(
        int levelId, int? academicYearId, CancellationToken cancellationToken)
    {
        var resolved = await yearResolver.ResolveAsync(academicYearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<Promotion>(resolved.Error);

        var year = await dbContext.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == resolved.Value, cancellationToken);

        if (year is null)
            return Result.Failure<Promotion>(Error.NotFound(
                "AcademicYears.NotFound", $"Année {resolved.Value} introuvable."));

        var level = await dbContext.Levels
            .FirstOrDefaultAsync(l => l.Id == levelId, cancellationToken);

        return level is null
            ? Result.Failure<Promotion>(RegistrationErrors.MissingLevel)
            : new Promotion(year, level, ExportLabels.Level(level.Label, level.Year, level.AcademicProgram));
    }
}
