using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Progression;

/// <summary>
/// « On ne commence pas la dernière année tant que tout ce qui précède n'est pas validé. »
///
/// <para>The rule is the faculty's, not an inference: a 7ᵉ année under arrêté 2174.18 and a 6ᵉ under
/// 1650.25 cannot be entered while a stage from an earlier year is still unvalidated. It is asked per
/// <b>student</b>, from his own text — from 2026-2027 one 6ᵉ année Médecine holds students of both, so
/// the level alone cannot answer "is this his last year?".</para>
///
/// <para><b>Why it lives here and not only in the réinscription.</b> The rollover is the path that
/// creates next year's registrations for a whole promotion, but it is not the only one:
/// <c>CreateRegistrationCommand</c> and <c>CreateManyRegistrationsCommand</c> can each put a student
/// in a level by hand. A guard the bulk path enforces and the manual path does not is a guard anyone
/// can step around by using the other button.</para>
///
/// <para><b>It stands aside where PGSH does not know.</b> No CNPN on record means no
/// <c>TotalYears</c>, and a student nobody has stamped must not be blocked by a number we do not
/// have — the same choice the déliberation makes for « Diplômé ». Likewise an unmarked stage is not a
/// failed one: see <see cref="OutstandingStageFinder"/>.</para>
/// </summary>
public sealed class FinalYearGuard(IApplicationDbContext dbContext, OutstandingStageFinder finder)
{
    /// <summary>
    /// Refuses if <paramref name="levelId"/> is the last year of this student's own cursus and he
    /// still owes a stage from a level below it, unless a waiver has been granted for
    /// <paramref name="academicYearId"/>.
    /// </summary>
    public async Task<Result> EnsureMayEnterAsync(
        Guid studentId, int levelId, int academicYearId, CancellationToken ct)
    {
        int levelYear = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => l.Year)
            .FirstOrDefaultAsync(ct);

        if (levelYear <= 0) return Result.Success();

        int? totalYears = await TotalYearsAsync(studentId, ct);
        if (totalYears is not { } total || levelYear < total)
            return Result.Success();

        var owed = (await finder.ForStudentAsync(studentId, ct))
            .Where(d => d.LevelYear < levelYear)
            .ToList();

        if (owed.Count == 0) return Result.Success();

        bool waived = await dbContext.FinalYearEntryWaivers
            .AsNoTracking()
            .AnyAsync(w => w.StudentId == studentId && w.AcademicYearId == academicYearId, ct);

        return waived
            ? Result.Success()
            : Result.Failure(RegistrationErrors.FinalYearBlocked(
                levelYear, owed.Count, OutstandingStageFinder.Summarize(owed)));
    }

    /// <summary>
    /// How long this student's text runs, read from his most recent registration's own CNPN and
    /// falling back to his stamp — the order used everywhere since the text became a property of the
    /// registration rather than of the student.
    /// </summary>
    public async Task<int?> TotalYearsAsync(Guid studentId, CancellationToken ct)
    {
        int? fromRegistration = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == studentId && r.CnpnVersionId != null)
            .OrderByDescending(r => r.AcademicYear.StartDate)
            .Select(r => (int?)r.CnpnVersion!.TotalYears)
            .FirstOrDefaultAsync(ct);

        if (fromRegistration is not null) return fromRegistration;

        return await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == studentId && s.CnpnVersionId != null)
            .Select(s => (int?)s.CnpnVersion!.TotalYears)
            .FirstOrDefaultAsync(ct);
    }
}
