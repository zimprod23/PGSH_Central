using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Progression;

/// <summary>
/// What a student still owes, across his whole cursus — the question « peut-il entamer sa dernière
/// année ? » reduces to.
///
/// <para><b>Cursus-wide is the point.</b> The déliberation's existing contradiction check looks only
/// at stages of the year being deliberated, so a 6ᵉ année student owing a 4ᵉ année stage is invisible
/// to it. A stage is not necessarily a criterion for failing a year — that is why it can be carried
/// forward at all — so the debt has to be read across every registration the student holds.</para>
///
/// <para><b>A stage is outstanding when every attempt at it came back <c>NonValidé</c></b> — the same
/// test <c>DossierStageState.ToRevalidate</c> uses, and deliberately the same: two screens disagreeing
/// about what a student owes is worse than either being slightly wrong.</para>
///
/// <para>⚠ <b>…counting only the attempts a year still stands behind.</b> A redoublant repeats the
/// year from scratch, stages included, so what he served inside the annulled year establishes
/// nothing either way — see <see cref="RegistrationStatusExtensions.AnnulsItsStages"/>. Without that
/// filter a pass in a failed year cleared the stage for good, and a student whose most recent attempt
/// was a failure could enter his final year on the strength of a year the faculty had struck out.
/// The filter never <i>creates</i> a debt: an annulled attempt is dropped, not counted as a
/// failure.</para>
///
/// <para>⚠ <b><c>NonÉvalué</c> does not count as owed.</b> An attempt with no verdict is a stage
/// nobody has marked, not a stage the student failed — and this base holds almost no marks, so
/// counting it would block the entire faculty from its final year on the strength of missing data
/// rather than of anything a student did. It is reported separately instead.</para>
///
/// <para>⚠ <b>Nor does a stage never attempted.</b> Reading « owes » from the CNPN's requirement set
/// would be stricter and, today, wrong: 1650.25's requirements are not entered, so every six-year
/// student would owe everything. When the requirement sets are complete this is the natural place to
/// widen, and the widening belongs here rather than at each call site.</para>
/// </summary>
public sealed class OutstandingStageFinder(IApplicationDbContext dbContext)
{
    /// <summary>One stage a student owes, and the year he was sitting when he owed it.</summary>
    public sealed record Debt(int StageId, string StageName, int LevelYear, string LevelLabel)
    {
        public override string ToString() => $"{StageName} ({LevelLabel})";
    }

    /// <summary>Everything one promotion still owes, per student. Students owing nothing are absent.</summary>
    /// <remarks>
    /// ⚠ Scoped by the <em>same predicate</em> that selects the promotion, never by shipping student
    /// ids back down — the déliberation learned this the expensive way: a year-wide run is 8 077
    /// registrations, and <c>ids.Contains(…)</c> turned a preview into a thirty-second query.
    /// </remarks>
    public async Task<Dictionary<Guid, IReadOnlyList<Debt>>> ForPromotionAsync(
        int academicYearId, int? levelId, CancellationToken ct)
    {
        var attempts = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => dbContext.Registrations.Any(r =>
                r.StudentId == a.Registration.StudentId
                && r.AcademicYearId == academicYearId
                && (levelId == null || r.LevelId == levelId)))
            .Select(a => new Attempt(
                a.Registration.StudentId,
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.Registration.Level.Year,
                a.Registration.Level.Label ?? string.Empty,
                a.Result,
                a.Registration.Status))
            .ToListAsync(ct);

        return Fold(attempts);
    }

    /// <summary>Everything a named set of students still owes. Students owing nothing are absent.</summary>
    /// <remarks>
    /// ⚠ <c>Contains</c> is right here and wrong in <see cref="ForPromotionAsync"/>, and the difference
    /// is not stylistic: this list is the caller's own — the students named in one bulk registration —
    /// so it is bounded by what somebody selected, while a promotion is 8 077 rows nobody enumerated.
    /// Reach for the predicate whenever the set is <em>described</em> rather than <em>listed</em>.
    /// </remarks>
    public async Task<Dictionary<Guid, IReadOnlyList<Debt>>> ForStudentsAsync(
        IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0) return [];

        var attempts = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => studentIds.Contains(a.Registration.StudentId))
            .Select(a => new Attempt(
                a.Registration.StudentId,
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.Registration.Level.Year,
                a.Registration.Level.Label ?? string.Empty,
                a.Result,
                a.Registration.Status))
            .ToListAsync(ct);

        return Fold(attempts);
    }

    /// <summary>The same question for one student — the single-registration paths and the dossier.</summary>
    public async Task<IReadOnlyList<Debt>> ForStudentAsync(Guid studentId, CancellationToken ct) =>
        (await ForStudentsAsync([studentId], ct)).GetValueOrDefault(studentId, []);

    private sealed record Attempt(
        Guid StudentId,
        int StageId,
        string StageName,
        int LevelYear,
        string LevelLabel,
        StageAssignmentResult? Result,
        RegistrationStatus YearOutcome);

    /// <summary>
    /// Groups attempts per (student, stage) and keeps the stages where <b>every</b> surviving attempt
    /// failed. One validated attempt clears the stage for good — a stage once acquired is never
    /// repeated, whichever year earned it — and one attempt still unmarked means the question is open
    /// rather than settled against the student.
    /// </summary>
    /// <remarks>
    /// Attempts made in an annulled year are dropped <b>before</b> the grouping, so a stage whose only
    /// attempts were annulled reads as never attempted rather than as owed. That is the right answer:
    /// the student is repeating the year and will serve it again, so he carries no debt from it.
    /// </remarks>
    private static Dictionary<Guid, IReadOnlyList<Debt>> Fold(IReadOnlyList<Attempt> attempts) =>
        attempts
            .Where(a => !a.YearOutcome.AnnulsItsStages())
            .GroupBy(a => (a.StudentId, a.StageId))
            .Where(g => g.All(a => a.Result == StageAssignmentResult.NonValidé))
            .GroupBy(g => g.Key.StudentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Debt>)g
                    // The earliest year it was owed in: that is the one that reads as the oldest debt,
                    // and it is what an operator scans the list for.
                    .Select(x => x.OrderBy(a => a.LevelYear).First())
                    .Select(a => new Debt(a.StageId, a.StageName, a.LevelYear, a.LevelLabel))
                    .OrderBy(d => d.LevelYear)
                    .ThenBy(d => d.StageName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

    /// <summary>« Cardiologie (3ème année), Pédiatrie (4ème année) » — capped, for a message.</summary>
    public static string Summarize(IReadOnlyList<Debt> debts, int max = 3)
    {
        if (debts.Count == 0) return string.Empty;

        string listed = string.Join(", ", debts.Take(max));
        return debts.Count > max ? $"{listed}, +{debts.Count - max}" : listed;
    }
}
