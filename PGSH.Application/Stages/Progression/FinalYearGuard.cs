using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Progression;

/// <summary>
/// « On ne commence pas la dernière année tant que tout ce qui précède n'est pas validé. »
///
/// <para>The rule is the faculty's, not an inference: a 7ᵉ année under arrêté 2174.18 and a 6ᵉ under
/// 1650.25 cannot be <b>entered</b> while a stage from an earlier year is still unvalidated. It is
/// asked per <b>student</b>, from his own text — from 2026-2027 one 6ᵉ année Médecine holds students
/// of both, so the level alone cannot answer "is this his last year?".</para>
///
/// <para>⚠ <b>« Entrer » is the whole rule, and reading it as « être inscrit en » inverts it.</b> The
/// final year is not a year one passes or fails: there is no déliberation for it. A student sits in
/// it, validates and revalidates his stages one at a time, and is <b>re-registered each September
/// until they are all validated</b> — and then re-registered again if he fails the examens cliniques,
/// which open as soon as the stages are done. So <em>the re-registration is the mechanism by which he
/// clears the debt</em>. Refusing it because he still owes a stage refuses him the only way to stop
/// owing it, and it fires hardest on the students who need it most.</para>
///
/// <para>Measured 2026-09-01 against the faculty's own réinscription roll for 2026-2027: of the 651
/// 7ᵉ année Médecine it re-registers into the 7ᵉ année, <b>182 were refused</b> — a quarter of the
/// promotion, every one of them named by the faculty as coming back. The gate now stands aside for a
/// student already registered at that level: he is continuing, not beginning.</para>
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
        var refusals = await EnsureMayEnterManyAsync([studentId], levelId, academicYearId, ct);

        return refusals.TryGetValue(studentId, out var error) ? Result.Failure(error) : Result.Success();
    }

    /// <summary>
    /// The same question for a named set of students, in a fixed number of round-trips. Only the
    /// students who are refused appear in the result; an absent student may enter.
    /// </summary>
    /// <remarks>
    /// <para>The single-student overload delegates here rather than the other way round, so there is
    /// one implementation of the decision. Asked per student it costs four queries each — the level's
    /// year, his text, his whole cursus and his waiver — which is ~2 800 round-trips to enrol a
    /// promotion of 700 through <c>CreateManyRegistrationsCommand</c>.</para>
    ///
    /// <para>The narrowing is what keeps the batch cheap and keeps the single call no dearer than it
    /// was: the cursus is read only for the students this level is actually the last year of, and the
    /// waivers only for those who turn out to owe something. A batch where nobody is in his final year
    /// — the ordinary case — is two queries whatever its size.</para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, Error>> EnsureMayEnterManyAsync(
        IReadOnlyCollection<Guid> studentIds, int levelId, int academicYearId, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0) return NoRefusals;

        int levelYear = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => l.Year)
            .FirstOrDefaultAsync(ct);

        if (levelYear <= 0) return NoRefusals;

        var totalYears = await TotalYearsAsync(ids, ct);

        // ⚠ Who is *beginning* this level, as opposed to continuing in it. A student who already
        // holds a registration at it is being re-registered to finish what he owes — which is how the
        // final year works — so the gate has nothing to say about him. See the class remarks: read as
        // « est inscrit en dernière année » rather than « entre en dernière année », this rule
        // refuses 182 of the 651 7ᵉ année Médecine the faculty's own roll brings back.
        var continuing = await AlreadyRegisteredAtLevelAsync(ids, levelId, ct);

        // ⚠ TryGetValue, not GetValueOrDefault: the dictionary holds `int`, so a student with no text
        // on record would read as "his cursus runs 0 years" and every year would be his last — which
        // blocked hardest exactly where the guard must stand aside.
        var entrants = ids
            .Where(id => !continuing.Contains(id))
            .Where(id => totalYears.TryGetValue(id, out int total) && levelYear >= total)
            .ToList();

        if (entrants.Count == 0) return NoRefusals;

        var debts = await finder.ForStudentsAsync(entrants, ct);

        var owing = new List<(Guid StudentId, List<OutstandingStageFinder.Debt> Owed)>();

        foreach (var id in entrants)
        {
            var owed = debts.GetValueOrDefault(id, [])
                .Where(d => d.LevelYear < levelYear)
                .ToList();

            if (owed.Count > 0) owing.Add((id, owed));
        }

        if (owing.Count == 0) return NoRefusals;

        var owingIds = owing.Select(x => x.StudentId).ToList();
        var waived = (await dbContext.FinalYearEntryWaivers
                .AsNoTracking()
                .Where(w => w.AcademicYearId == academicYearId && owingIds.Contains(w.StudentId))
                .Select(w => w.StudentId)
                .ToListAsync(ct))
            .ToHashSet();

        return owing
            .Where(x => !waived.Contains(x.StudentId))
            .ToDictionary(
                x => x.StudentId,
                x => RegistrationErrors.FinalYearBlocked(
                    levelYear, x.Owed.Count, OutstandingStageFinder.Summarize(x.Owed)));
    }

    /// <summary>
    /// Which of these students has already been registered at this level.
    /// </summary>
    /// <remarks>
    /// <para>The discriminator between « il commence sa dernière année » and « il la continue », and
    /// it is read from the store rather than passed in because every caller would otherwise have to
    /// carry the previous level — and two of the four do not have it. A student who sat in the level
    /// before is continuing whatever gap there has been: a 7ᵉ année who dropped out in 2023-2024 and
    /// comes back in 2026-2027 still has the same stages to revalidate, which is exactly what the
    /// re-registration is for.</para>
    ///
    /// <para>⚠ <c>Contains</c> on the ids is right here: they are the batch the caller handed in, a
    /// <em>listed</em> set. The debt lookup below is scoped the same way for the same reason.</para>
    /// </remarks>
    private async Task<HashSet<Guid>> AlreadyRegisteredAtLevelAsync(
        IReadOnlyCollection<Guid> studentIds, int levelId, CancellationToken ct) =>
        (await AlreadyRegisteredAtLevelQuery(dbContext, studentIds, levelId).ToListAsync(ct))
            .ToHashSet();

    internal static IQueryable<Guid> AlreadyRegisteredAtLevelQuery(
        IApplicationDbContext db, IReadOnlyCollection<Guid> studentIds, int levelId) =>
        db.Registrations
            .AsNoTracking()
            .Where(r => r.LevelId == levelId && studentIds.Contains(r.StudentId))
            .Select(r => r.StudentId)
            .Distinct();

    /// <summary>
    /// How long this student's text runs, read from his most recent registration's own CNPN and
    /// falling back to his stamp — the order used everywhere since the text became a property of the
    /// registration rather than of the student.
    /// </summary>
    public async Task<int?> TotalYearsAsync(Guid studentId, CancellationToken ct) =>
        (await TotalYearsAsync([studentId], ct)).TryGetValue(studentId, out int total) ? total : null;

    /// <summary>
    /// The same, for a set of students. Absent means PGSH holds no text for him, which is not zero:
    /// ~2 200 enrolled students carry no stamp at all.
    /// </summary>
    private async Task<Dictionary<Guid, int>> TotalYearsAsync(
        IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        // The registration rows are folded here rather than in SQL: "the latest registration carrying
        // a text, per student" is a grouped top-1, and each student holds at most a handful of them.
        var fromRegistrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => studentIds.Contains(r.StudentId) && r.CnpnVersionId != null)
            .Select(r => new
            {
                r.StudentId,
                r.AcademicYear.StartDate,
                r.CnpnVersion!.TotalYears
            })
            .ToListAsync(ct);

        var byStudent = fromRegistrations
            .GroupBy(r => r.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.StartDate).First().TotalYears);

        var fromStamps = await dbContext.Students
            .AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && s.CnpnVersionId != null)
            .Select(s => new { s.Id, s.CnpnVersion!.TotalYears })
            .ToListAsync(ct);

        // The registration wins where both are present: it is the text that governs the year he is
        // coming out of, and the stamp is only what he happens to be on now.
        foreach (var stamp in fromStamps)
            byStudent.TryAdd(stamp.Id, stamp.TotalYears);

        return byStudent;
    }

    private static readonly IReadOnlyDictionary<Guid, Error> NoRefusals =
        new Dictionary<Guid, Error>();
}
