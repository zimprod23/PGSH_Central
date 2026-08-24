using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn;

/// <summary>
/// Stamps the governing CNPN onto registrations, in batch, for every path that creates them —
/// inscription, inscription en masse, réinscription — and for the one that re-stamps them after a
/// rule was authored too late.
///
/// <para><b>Why this is not a loop over <see cref="CnpnResolver"/>.</b> A réinscription creates
/// several hundred registrations in one act. Resolving each one on its own is four queries a
/// student; the population's stamps, prior texts and the year's effectivity rules are three lookups
/// for the whole batch. Only the last resort — a student with no stamp, no history and therefore no
/// recorded entry — costs a query of its own, and by construction there are few of those.</para>
///
/// <para>⚠ <b>A registration being created is its own entry evidence.</b> <see cref="CnpnAssignment"/>
/// resolves entry from recorded registrations and fails when there are none — which is exactly the
/// state of a genuine new entrant whose first registration has not been saved yet. Rather than save
/// twice, the pending registration is treated as the earliest one, which is what it is.</para>
/// </summary>
public sealed class RegistrationCnpnStamper(
    IApplicationDbContext dbContext,
    CnpnAssignment assignment)
{
    /// <summary>
    /// What a stamping pass did. <paramref name="Unresolved"/> is not an error: a student for whom no
    /// text can be determined keeps a null stamp, which every reader falls back on gracefully, and
    /// stamping him with a guess would be worse.
    /// </summary>
    public sealed record StampReport(
        int Stamped,
        int Changed,
        int ByEffectivity,
        int StudentsMoved,
        IReadOnlyList<Guid> Unresolved,
        IReadOnlyList<Guid> FrozenByOutcome);

    public async Task<Result<StampReport>> StampAsync(
        IReadOnlyList<Registration> registrations, CancellationToken ct)
    {
        if (registrations.Count == 0)
            return new StampReport(0, 0, 0, 0, [], []);

        var levelIds = registrations.Select(r => r.LevelId).Distinct().ToList();
        var yearIds = registrations.Select(r => r.AcademicYearId).Distinct().ToList();
        var studentIds = registrations.Select(r => r.StudentId).Distinct().ToList();

        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .OrderBy(y => y.StartDate)
            .Select(y => new YearRef(y.Id, y.StartDate))
            .ToListAsync(ct);

        var levels = await dbContext.Levels
            .AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Year, l.AcademicProgram })
            .ToDictionaryAsync(l => l.Id, l => (l.Year, l.AcademicProgram), ct);

        var effectivity = await LoadEffectivityAsync(levelIds, yearIds, years, ct);

        // Tracked: an effectivity rule has to be able to advance the student's own stamp.
        var students = await dbContext.Students
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var pendingIds = registrations.Select(r => r.Id).ToList();

        var priorStamps = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => studentIds.Contains(r.StudentId)
                         && r.CnpnVersionId != null
                         && !pendingIds.Contains(r.Id))
                .Select(r => new { r.StudentId, r.CnpnVersionId, r.AcademicYear.StartDate })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartDate).First().CnpnVersionId!.Value);

        var earliestRecorded = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => studentIds.Contains(r.StudentId) && !pendingIds.Contains(r.Id))
                .Select(r => new { r.StudentId, r.AcademicYearId, r.AcademicYear.StartDate, LevelYear = r.Level.Year })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.StartDate)
                      .Select(r => new EntryRef(r.AcademicYearId, r.LevelYear))
                      .First());

        int stamped = 0, changed = 0, byRule = 0, moved = 0;
        var unresolved = new List<Guid>();
        var frozen = new List<Guid>();

        foreach (var registration in registrations)
        {
            var decided = effectivity.TryGetValue((registration.LevelId, registration.AcademicYearId), out int ruled)
                ? new Decision(ruled, RegistrationCnpnSource.Effectivity)
                : Fallback(registration, students, priorStamps);

            if (decided is null)
            {
                decided = await ResolveFromEntryAsync(
                    registration, levels, years, earliestRecorded, ct);
            }

            if (decided is null)
            {
                unresolved.Add(registration.StudentId);
                continue;
            }

            int? before = registration.CnpnVersionId;

            var write = registration.StampCnpnVersion(decided.CnpnVersionId, decided.Source);
            if (write.IsFailure)
            {
                frozen.Add(registration.StudentId);
                continue;
            }

            stamped++;
            if (before != decided.CnpnVersionId) changed++;

            if (decided.Source != RegistrationCnpnSource.Effectivity)
                continue;

            byRule++;

            // The rule is the faculty saying "these people are now on this text", so it reaches the
            // student's own stamp too — otherwise TotalYears, and therefore how many years he owes,
            // would still be read from the text he just left. This is the one path allowed to move a
            // confirmed stamp: it was authored for this exact (level, year) and fires on one
            // registration at the moment it is created, not over a population re-selected each year.
            if (students.TryGetValue(registration.StudentId, out var student)
                && student.CnpnVersionId != decided.CnpnVersionId)
            {
                var advance = student.AssignCnpnVersion(
                    decided.CnpnVersionId, isInferred: false, overrideExisting: true);

                if (advance.IsSuccess) moved++;
            }
        }

        return new StampReport(stamped, changed, byRule, moved, unresolved, frozen);
    }

    private sealed record Decision(int CnpnVersionId, RegistrationCnpnSource Source);

    private sealed record YearRef(int Id, DateOnly StartDate);

    /// <summary>The earliest registration PGSH holds for a student — the entry the arrêté keys on.</summary>
    private sealed record EntryRef(int AcademicYearId, int LevelYear);

    private static Decision? Fallback(
        Registration registration,
        IReadOnlyDictionary<Guid, Student> students,
        IReadOnlyDictionary<Guid, int> priorStamps)
    {
        if (students.TryGetValue(registration.StudentId, out var student)
            && student.CnpnVersionId is { } stamp)
            return new Decision(stamp, RegistrationCnpnSource.StudentStamp);

        return priorStamps.TryGetValue(registration.StudentId, out int carried)
            ? new Decision(carried, RegistrationCnpnSource.CarriedForward)
            : null;
    }

    /// <summary>
    /// Last resort: the text governing the intake this student entered on. Entry comes from his
    /// earliest recorded registration when he has one, and otherwise from the registration being
    /// created — a first registration at level 1 is a genuine entry, and at any higher level the real
    /// entry is (level - 1) years earlier, which is the same deduction <see cref="CnpnAssignment"/>
    /// makes for the ~2,200 students the legacy import caught mid-cursus.
    /// </summary>
    private async Task<Decision?> ResolveFromEntryAsync(
        Registration registration,
        IReadOnlyDictionary<int, (int Year, AcademicProgram Program)> levels,
        IReadOnlyList<YearRef> years,
        IReadOnlyDictionary<Guid, EntryRef> earliestRecorded,
        CancellationToken ct)
    {
        if (!levels.TryGetValue(registration.LevelId, out var level))
            return null;

        int anchorYearId = registration.AcademicYearId;
        int anchorLevelYear = level.Year;

        if (earliestRecorded.TryGetValue(registration.StudentId, out var earliest))
        {
            anchorYearId = earliest.AcademicYearId;
            anchorLevelYear = earliest.LevelYear;
        }

        int entryYearId = anchorLevelYear <= 1
            ? anchorYearId
            : WalkBack(years, anchorYearId, anchorLevelYear);

        var version = await assignment.SelectVersionAsync(level.Program, entryYearId, ct);

        return version.IsFailure
            ? null
            : new Decision(version.Value, RegistrationCnpnSource.ResolvedFromEntry);
    }

    private static int WalkBack(IReadOnlyList<YearRef> orderedYears, int fromYearId, int levelYear)
    {
        int index = -1;
        for (int i = 0; i < orderedYears.Count; i++)
            if (orderedYears[i].Id == fromYearId) { index = i; break; }

        return index < 0 ? fromYearId : orderedYears[Math.Max(0, index - (levelYear - 1))].Id;
    }

    /// <summary>
    /// (level, year) → the text in force, for every combination the batch touches. Resolution is the
    /// rule for that level with the latest start date at or before the year's, which is why the rows
    /// are compared on dates rather than on ids.
    /// </summary>
    private async Task<Dictionary<(int LevelId, int AcademicYearId), int>> LoadEffectivityAsync(
        IReadOnlyList<int> levelIds,
        IReadOnlyList<int> yearIds,
        IReadOnlyList<YearRef> years,
        CancellationToken ct)
    {
        var rows = await dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => levelIds.Contains(e.LevelId))
            .Select(e => new { e.LevelId, e.CnpnVersionId, From = e.FromAcademicYear.StartDate })
            .ToListAsync(ct);

        var map = new Dictionary<(int, int), int>();
        if (rows.Count == 0) return map;

        var yearStart = years.ToDictionary(y => y.Id, y => y.StartDate);

        foreach (int yearId in yearIds)
        {
            if (!yearStart.TryGetValue(yearId, out var start)) continue;

            foreach (int levelId in levelIds)
            {
                var governing = rows
                    .Where(r => r.LevelId == levelId && r.From <= start)
                    .OrderByDescending(r => r.From)
                    .FirstOrDefault();

                if (governing is not null)
                    map[(levelId, yearId)] = governing.CnpnVersionId;
            }
        }

        return map;
    }
}
