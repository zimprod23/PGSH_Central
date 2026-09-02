using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.SeedFromHistory;

/// <summary>
/// Attributes a governing text to every imported student and to every registration he holds — the
/// pass that follows a legacy import, and the one a rebuilt database silently goes without.
///
/// <para><b>Why it exists as code rather than as the migration that first did it.</b> The student
/// attribution was a single <c>UPDATE</c> inside <c>CnpnVersioning</c> (2026-08-08) and the
/// registration backfill another inside <c>RegistrationCnpnAndLevelEffectivity</c> (2026-08-18). Both
/// were written to run <em>over</em> data that was already there. Replayed against a database rebuilt
/// from the .mdb they do nothing at all — the migration chain runs before the import, so there is
/// nobody to stamp — and they are then marked applied, so nothing will ever run them again. The
/// result is a base where 10 200 students and 49 500 registrations carry a null text, which every
/// reader tolerates gracefully and therefore nothing complains about: the déliberation stops knowing
/// whose year might be his last, the final-year gate stands aside for everyone, and
/// <c>CohortProvisioner</c> plans against requirement sets nobody is bound by.</para>
///
/// <para><b>The rule is not restated here.</b> Entry is <see cref="EntryYearDeduction"/>'s —
/// « on ne peut pas être en 3ᵉ année sans avoir passé deux ans » — and which text governs an intake is
/// <see cref="CnpnAssignment"/>'s. This only walks the population and applies them, so a re-import
/// cannot land students on a different rule from the one the application uses every day.</para>
///
/// <para>⚠ <b>And it refuses rather than reporting a total no-op.</b> If not one student could be
/// placed, the catalogue has no selectable text in it — every <c>CnpnVersion</c> is citation-only,
/// which is what a rebuild leaves behind when <c>CnpnVersioning</c> runs before the import and reads
/// its intake years out of an empty <c>AcademicYears</c>. Nothing else in the chain notices, because
/// a text with no intake year is a legitimate state rather than a malformed row.</para>
///
/// <para>⚠ <b>It never moves a confirmed stamp.</b> <c>Student.AssignCnpnVersion</c> refuses that
/// without <c>overrideExisting</c>, and nothing here passes it: this pass exists to fill a blank
/// base, and re-running it over a base where scolarité has confirmed assignments must leave those
/// alone. Upgrading an <em>inferred</em> stamp is not a move and is allowed, which is what lets it be
/// re-run after more history arrives.</para>
///
/// <para>⚠ <b>And it never overwrites a registration's own stamp.</b>
/// <c>Registration.CnpnVersionId</c> is what the student owed <em>that year</em>, which is not his
/// current stamp restated — so only null ones are written, marked
/// <see cref="RegistrationCnpnSource.Backfilled"/>, deliberately not <c>StudentStamp</c>: nobody was
/// asked at the time.</para>
///
/// <para>A closed year is <b>not</b> skipped, and the distinction matters.
/// <c>CnpnFrozenByOutcome</c> refuses to <em>move</em> a stamp once a verdict stands against it —
/// a verdict whose obligations shifted afterwards is not readable. Recording one where there was
/// none moves nothing, and the alternative is a pronounced year that can never say what it was
/// judged against.</para>
/// </summary>
public sealed class CnpnHistoryAttributor(IApplicationDbContext dbContext, CnpnAssignment assignment)
{
    public async Task<Result<CnpnAttributionReport>> AttributeAsync(bool dryRun, CancellationToken ct)
    {
        var years = (await dbContext.AcademicYears
                .AsNoTracking()
                .OrderBy(y => y.StartDate)
                .Select(y => new EntryYearDeduction.AcademicYearRef(y.Id, y.StartDate))
                .ToListAsync(ct))
            .ToList();

        if (years.Count == 0)
            return Result.Failure<CnpnAttributionReport>(CnpnErrors.NoAcademicYears);

        // The earliest registration each student holds, and the level year he sat in that year —
        // the two facts entry is deduced from. One flat read keyed on nothing, folded in memory: the
        // per-student « first row by date » is a grouped top-1, and each student holds a handful.
        var enrolments = await EnrolmentsQuery(dbContext).ToListAsync(ct);

        var earliest = enrolments
            .GroupBy(e => e.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.StartDate).First());

        // Tracked: AssignCnpnVersion is the only writer, and it is on the aggregate.
        var students = await dbContext.Students.ToDictionaryAsync(s => s.Id, ct);

        int stamped = 0, alreadySettled = 0, inferred = 0;
        var unresolved = new List<Guid>();

        foreach (var (studentId, first) in earliest)
        {
            if (!students.TryGetValue(studentId, out var student)) continue;

            if (student.CnpnVersionId is not null && !student.CnpnAssignmentIsInferred)
            {
                alreadySettled++;
                continue;
            }

            int entryYearId = EntryYearDeduction.EntryYearId(years, first.AcademicYearId, first.LevelYear);
            bool isInferred = !EntryYearDeduction.IsRecordedEntry(first.LevelYear);

            var version = await assignment.SelectVersionAsync(first.Program, entryYearId, ct);
            if (version.IsFailure)
            {
                // Not an error. A programme with no text recorded for that intake leaves the student
                // unstamped, which every reader falls back on — and stamping him with a guess would
                // be worse than leaving the fact absent.
                unresolved.Add(studentId);
                continue;
            }

            if (student.AssignCnpnVersion(version.Value, isInferred).IsFailure)
            {
                alreadySettled++;
                continue;
            }

            stamped++;
            if (isInferred) inferred++;
        }

        // ⚠ Nobody placed at all is not « a lot of unresolved students », it is a catalogue with no
        // selectable text in it — and because a text without an intake year is a legitimate state
        // (citation-only), nothing else in the chain says so. Refused before the registrations are
        // touched, so a rebuild stops here rather than reporting a total no-op as a success.
        if (stamped == 0 && alreadySettled == 0 && unresolved.Count > 0)
            return Result.Failure<CnpnAttributionReport>(
                CnpnErrors.NoTextGovernsAnyIntake(unresolved.Count));

        // The registrations. Tracked for the same reason, and only the null ones are written.
        var registrations = await dbContext.Registrations
            .Where(r => r.CnpnVersionId == null)
            .ToListAsync(ct);

        int backfilled = 0;
        var refused = new List<Guid>();

        foreach (var registration in registrations)
        {
            if (!students.TryGetValue(registration.StudentId, out var student)
                || student.CnpnVersionId is not { } versionId)
                continue;

            // ⚠ Normally zero: the only refusal StampCnpnVersion can return needs a stamp already
            // present, and this loop reads the null ones. The Result is still checked rather than
            // discarded — the aggregate is the authority on its own invariant, and a caller that
            // assumed its filter made a refusal impossible is how the next invariant gets ignored.
            if (registration.StampCnpnVersion(versionId, RegistrationCnpnSource.Backfilled).IsFailure)
            {
                refused.Add(registration.Id);
                continue;
            }

            backfilled++;
        }

        if (!dryRun)
            await dbContext.SaveChangesAsync(ct);

        return new CnpnAttributionReport(
            StudentsConsidered: earliest.Count,
            StudentsStamped: stamped,
            StudentsInferred: inferred,
            StudentsAlreadySettled: alreadySettled,
            StudentsUnresolved: unresolved.Count,
            RegistrationsBackfilled: backfilled,
            RegistrationsRefusedByAggregate: refused.Count,
            DryRun: dryRun);
    }

    /// <summary>
    /// Every registration reduced to what the deduction needs. Named and <c>internal static</c> for
    /// the usual reason: a query buried in a private async method cannot be handed to
    /// <c>ToQueryString()</c>, and the in-memory provider translates nothing.
    /// </summary>
    internal static IQueryable<Enrolment> EnrolmentsQuery(IApplicationDbContext db) =>
        db.Registrations
            .AsNoTracking()
            .Select(r => new Enrolment(
                r.StudentId,
                r.AcademicYearId,
                r.AcademicYear.StartDate,
                r.Level.Year,
                r.Level.AcademicProgram));

    internal sealed record Enrolment(
        Guid StudentId,
        int AcademicYearId,
        DateOnly StartDate,
        int LevelYear,
        Domain.Common.Utils.AcademicProgram Program);
}

/// <summary>
/// What an attribution pass did.
/// </summary>
/// <param name="StudentsUnresolved">
/// Students no recorded text covers. Not an error: null means « jamais résolu », every reader falls
/// back on it, and stamping a guess would be worse than leaving the fact absent.
/// </param>
/// <param name="RegistrationsRefusedByAggregate">
/// Registrations <c>Registration.StampCnpnVersion</c> refused. Expected to be zero — see the loop
/// that fills it — and reported rather than swallowed precisely so that stops being true loudly.
/// </param>
public sealed record CnpnAttributionReport(
    int StudentsConsidered,
    int StudentsStamped,
    int StudentsInferred,
    int StudentsAlreadySettled,
    int StudentsUnresolved,
    int RegistrationsBackfilled,
    int RegistrationsRefusedByAggregate,
    bool DryRun);
