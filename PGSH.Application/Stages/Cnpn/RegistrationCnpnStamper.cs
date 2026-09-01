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
/// <para><b>Why this is not a loop over a per-student resolver.</b> A réinscription creates
/// several hundred registrations in one act. Resolving each one on its own is four queries a
/// student; the population's stamps, prior texts and the year's effectivity rules are three lookups
/// for the whole batch. Only the last resort — a student with no stamp, no history and therefore no
/// recorded entry — costs a query of its own, and by construction there are few of those.</para>
///
/// <para>⚠ <b>A registration being created is its own entry evidence.</b> Entry is normally read
/// from recorded registrations, and a genuine new entrant has none — his first has not been saved
/// yet. Rather than save twice, the pending registration is treated as the earliest one, which is
/// what it is.</para>
/// </summary>
/// <remarks>
/// <c>internal</c>, and the rule is not arbitrary: <see cref="CnpnAssignment"/> and
/// <c>CurriculumHistoryReconstructor</c> are public because <c>PGSH.LegacyImport</c> builds them by
/// hand to run the same derivation without an HTTP identity. Nothing outside this assembly stamps a
/// registration, so nothing outside it needs to see this.
/// </remarks>
internal sealed class RegistrationCnpnStamper(
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

    /// <remarks>
    /// Returns a report rather than a <c>Result</c>: there is no failure to return. Every refusal
    /// this pass can meet is a fact about one registration — no text could be resolved, or the year
    /// is already pronounced — and stopping the batch on one of them would refuse the other six
    /// hundred. They are counted and named instead, and the caller decides what to say about them.
    /// </remarks>
    public async Task<StampReport> StampAsync(
        IReadOnlyList<Registration> registrations, CancellationToken ct)
    {
        if (registrations.Count == 0)
            return new StampReport(0, 0, 0, 0, [], []);

        var levelIds = registrations.Select(r => r.LevelId).Distinct().ToList();
        var yearIds = registrations.Select(r => r.AcademicYearId).Distinct().ToList();
        var studentIds = registrations.Select(r => r.StudentId).Distinct().ToList();

        var years = await AcademicYearsQuery(dbContext).ToListAsync(ct);

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

        // `Contains` is right here and wrong in CnpnTargetPlanner, and the difference is not stylistic:
        // these ids are the batch the caller handed in — a *listed* set, bounded by what somebody
        // selected — while a promotion is a set nobody enumerated and reaches the store as a predicate.
        var priorStamps = (await PriorStampsQuery(dbContext, studentIds, pendingIds).ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartDate).First().CnpnVersionId);

        var earliestRecorded = (await PriorRegistrationsQuery(dbContext, studentIds, pendingIds).ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.StartDate)
                      .Select(r => new EntryRef(r.AcademicYearId, r.LevelYear))
                      .First());

        var stampPrograms = await StampProgramsAsync(students.Values, priorStamps.Values, ct);

        int stamped = 0, changed = 0, byRule = 0, moved = 0;
        var unresolved = new List<Guid>();
        var frozen = new List<Guid>();

        foreach (var registration in registrations)
        {
            var decided = effectivity.TryGetValue((registration.LevelId, registration.AcademicYearId), out int ruled)
                ? new Decision(ruled, RegistrationCnpnSource.Effectivity)
                : Fallback(registration, students, priorStamps, levels, stampPrograms);

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

    /// <summary>The earliest registration PGSH holds for a student — the entry the arrêté keys on.</summary>
    private sealed record EntryRef(int AcademicYearId, int LevelYear);

    /// <summary>
    /// The student's own stamp, then the text on his most recent earlier registration — <b>as long as
    /// either governs the programme he is registering in</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A carried stamp is only an answer while the student stays in his programme.</b> A
    /// <c>CnpnVersion</c> belongs to exactly one <see cref="AcademicProgram"/>, so on a réorientation —
    /// Médecine → Pharmacie — carrying it forward stamps the registration with a text that says
    /// nothing about the cursus he has just entered, and <c>TotalYears</c> read from it then answers
    /// « est-ce sa dernière année ? » from the wrong arrêté. Refusing the mismatch is not a loss: it
    /// falls through to <see cref="ResolveFromEntryAsync"/>, which resolves from the level's own
    /// programme and is exactly the deduction wanted here.
    /// </remarks>
    private static Decision? Fallback(
        Registration registration,
        IReadOnlyDictionary<Guid, Student> students,
        IReadOnlyDictionary<Guid, int> priorStamps,
        IReadOnlyDictionary<int, (int Year, AcademicProgram Program)> levels,
        IReadOnlyDictionary<int, AcademicProgram> stampPrograms)
    {
        // A level we cannot read is a level we cannot contradict: keep the stamp rather than discard
        // a correct answer over a lookup miss.
        AcademicProgram? target = levels.TryGetValue(registration.LevelId, out var level)
            ? level.Program
            : null;

        bool Governs(int cnpnVersionId) =>
            target is null
            || !stampPrograms.TryGetValue(cnpnVersionId, out var program)
            || program == target;

        if (students.TryGetValue(registration.StudentId, out var student)
            && student.CnpnVersionId is { } stamp
            && Governs(stamp))
            return new Decision(stamp, RegistrationCnpnSource.StudentStamp);

        return priorStamps.TryGetValue(registration.StudentId, out int carried) && Governs(carried)
            ? new Decision(carried, RegistrationCnpnSource.CarriedForward)
            : null;
    }

    /// <summary>
    /// Which programme each candidate stamp governs. Read only for the versions the batch could
    /// actually carry forward — a handful of ids, whatever the size of the population.
    /// </summary>
    private async Task<Dictionary<int, AcademicProgram>> StampProgramsAsync(
        IEnumerable<Student> students, IEnumerable<int> priorStamps, CancellationToken ct)
    {
        var ids = students
            .Select(s => s.CnpnVersionId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Concat(priorStamps)
            .Distinct()
            .ToList();

        if (ids.Count == 0) return [];

        return await StampProgramsQuery(dbContext, ids)
            .ToDictionaryAsync(s => s.CnpnVersionId, s => s.Program, ct);
    }

    /// <summary>
    /// Last resort: the text governing the intake this student entered on. Entry comes from his
    /// earliest recorded registration when he has one, and otherwise from the registration being
    /// created — a first registration at level 1 is a genuine entry, and at any higher level the real
    /// entry is (level - 1) years earlier — <see cref="EntryYearDeduction"/>, the same walk-back the
    /// backfill made for the ~2,200 students the legacy import caught mid-cursus.
    /// </summary>
    private async Task<Decision?> ResolveFromEntryAsync(
        Registration registration,
        IReadOnlyDictionary<int, (int Year, AcademicProgram Program)> levels,
        IReadOnlyList<EntryYearDeduction.AcademicYearRef> years,
        IReadOnlyDictionary<Guid, EntryRef> earliestRecorded,
        CancellationToken ct)
    {
        if (!levels.TryGetValue(registration.LevelId, out var level))
            return null;

        var anchor = earliestRecorded.TryGetValue(registration.StudentId, out var earliest)
            ? earliest
            : new EntryRef(registration.AcademicYearId, level.Year);

        int entryYearId = EntryYearDeduction.EntryYearId(years, anchor.AcademicYearId, anchor.LevelYear);

        var version = await assignment.SelectVersionAsync(level.Program, entryYearId, ct);

        return version.IsFailure
            ? null
            : new Decision(version.Value, RegistrationCnpnSource.ResolvedFromEntry);
    }

    /// <summary>
    /// (level, year) → the text in force, for every combination the batch touches. Resolution is the
    /// rule for that level with the latest start date at or before the year's, which is why the rows
    /// are compared on dates rather than on ids.
    /// </summary>
    private async Task<Dictionary<(int LevelId, int AcademicYearId), int>> LoadEffectivityAsync(
        IReadOnlyList<int> levelIds,
        IReadOnlyList<int> yearIds,
        IReadOnlyList<EntryYearDeduction.AcademicYearRef> years,
        CancellationToken ct)
    {
        var rows = await EffectivityRulesQuery(dbContext, levelIds).ToListAsync(ct);

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

    // =============================================================================================
    // The reads, named so they can be compiled without a database
    // =============================================================================================
    //
    // A query buried in a private async method cannot be handed to ToQueryString(), and the in-memory
    // provider translates nothing — so an untranslatable shape would surface on the first real
    // réinscription, which is the single act that stamps a whole promotion. Every one of these is
    // deliberately flat: read, then fold in memory. Nesting the grouping into the projection is the
    // collection-subquery shape Npgsql refuses, and it is what killed the macro plan.
    // Pinned by SqlTranslationTests.

    /// <summary>Every academic year, ordered — what the entry deduction walks back through.</summary>
    internal static IQueryable<EntryYearDeduction.AcademicYearRef> AcademicYearsQuery(
        IApplicationDbContext dbContext) =>
        dbContext.AcademicYears
            .AsNoTracking()
            .OrderBy(y => y.StartDate)
            .Select(y => new EntryYearDeduction.AcademicYearRef(y.Id, y.StartDate));

    internal sealed record PriorStamp(Guid StudentId, int CnpnVersionId, DateOnly StartDate);

    /// <summary>The texts already stamped on these students' other registrations.</summary>
    internal static IQueryable<PriorStamp> PriorStampsQuery(
        IApplicationDbContext dbContext, IReadOnlyList<Guid> studentIds, IReadOnlyList<Guid> pendingIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => studentIds.Contains(r.StudentId)
                     && r.CnpnVersionId != null
                     && !pendingIds.Contains(r.Id))
            .Select(r => new PriorStamp(r.StudentId, r.CnpnVersionId!.Value, r.AcademicYear.StartDate));

    internal sealed record PriorRegistration(
        Guid StudentId, int AcademicYearId, DateOnly StartDate, int LevelYear);

    /// <summary>Their other registrations, for the earliest one — the entry the arrêté keys on.</summary>
    internal static IQueryable<PriorRegistration> PriorRegistrationsQuery(
        IApplicationDbContext dbContext, IReadOnlyList<Guid> studentIds, IReadOnlyList<Guid> pendingIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => studentIds.Contains(r.StudentId) && !pendingIds.Contains(r.Id))
            .Select(r => new PriorRegistration(
                r.StudentId, r.AcademicYearId, r.AcademicYear.StartDate, r.Level.Year));

    internal sealed record StampProgram(int CnpnVersionId, AcademicProgram Program);

    /// <summary>Which programme each candidate stamp governs — a handful of ids, whatever the batch.</summary>
    internal static IQueryable<StampProgram> StampProgramsQuery(
        IApplicationDbContext dbContext, IReadOnlyList<int> cnpnVersionIds) =>
        dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => cnpnVersionIds.Contains(v.Id))
            .Select(v => new StampProgram(v.Id, v.AcademicProgram));

    internal sealed record EffectivityRule(int LevelId, int CnpnVersionId, DateOnly From);

    /// <summary>
    /// Every rule authored for the levels this batch touches. ⚠ Compared on <c>StartDate</c> rather
    /// than on year ids, because « la règle en vigueur » is the latest one at or before the
    /// registration's year and ids carry no order.
    /// </summary>
    internal static IQueryable<EffectivityRule> EffectivityRulesQuery(
        IApplicationDbContext dbContext, IReadOnlyList<int> levelIds) =>
        dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => levelIds.Contains(e.LevelId))
            .Select(e => new EffectivityRule(e.LevelId, e.CnpnVersionId, e.FromAcademicYear.StartDate));
}
