using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// Writes a plan: the students, their registrations, the équivalences, and the CNPN stamps. One
/// <c>SaveChanges</c>, so a promotion is inscribed entirely or not at all.
///
/// <para><b>Why it is a class and not the body of the handler.</b> There are two ways in — a sheet
/// for the September intake, and a form for the transfer notified in November — and they differ only
/// in how the rows arrive and whether a count has to be confirmed. Sharing the planner alone would
/// still leave two copies of the writes, and it is the writes that create identities. Same reason
/// <c>FinalYearGuard.EnsureMayEnterManyAsync</c> is the implementation and the single-student call
/// delegates to it.</para>
/// </summary>
internal sealed class InscriptionApplier(
    IApplicationDbContext dbContext,
    RegistrationCnpnStamper stamper,
    ExecutionAuthorizer authorizer)
{
    public async Task<Result> ApplyAsync(InscriptionPlan plan, CancellationToken ct)
    {
        var work = plan.Drafts.Where(d => d.Action.Writes()).ToList();
        if (work.Count == 0)
            return Result.Success();

        // Tracked, and only for the rows that already name somebody: a réorientation writes to the
        // student row itself, so the entity has to be the one the context is watching.
        var existing = await TrackedStudentsAsync(work, ct);
        var recordedBy = await authorizer.CurrentUserIdAsync(ct);

        var registrations = new List<Registration>(work.Count);
        var origins = new List<PriorEnrolment>();
        var reoriented = new List<Guid>();
        var now = DateTime.UtcNow;

        foreach (var draft in work)
        {
            Guid studentId;

            if (draft.CreatesStudent)
            {
                var student = BuildStudent(draft, plan.Programme);
                dbContext.Students.Add(student);
                studentId = student.Id;
            }
            else
            {
                studentId = draft.Student!.Id;

                if (draft.Action == InscriptionAction.ProgrammeChange
                    && existing.TryGetValue(studentId, out var tracked))
                {
                    tracked.AcademicProgram = plan.Programme;
                    reoriented.Add(studentId);
                }
            }

            var registration = new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                AcademicYearId = plan.AcademicYearId,
                LevelId = plan.LevelId,
                Status = RegistrationStatus.Pending,
                RegistrationDate = now,
            };

            registration.Raise(new StudentRegisteredDomainEvent(
                registration.Id, studentId, plan.LevelId, plan.AcademicYearId));

            registrations.Add(registration);

            if (draft.Origin is { } origin)
            {
                origins.Add(new PriorEnrolment
                {
                    Id = Guid.NewGuid(),
                    RegistrationId = registration.Id,
                    Institution = origin.Institution,
                    Country = origin.Country,
                    LastLevelYearCompleted = origin.LastLevelYearCompleted,
                    EquivalenceReference = origin.EquivalenceReference,
                    EquivalenceDate = origin.EquivalenceDate,
                    RecordedByUserId = recordedBy,
                    RecordedOn = now,
                });
            }
        }

        dbContext.Registrations.AddRange(registrations);
        if (origins.Count > 0) dbContext.PriorEnrolments.AddRange(origins);

        // One pass for the whole promotion, exactly as the réinscription does it. A newcomer carries
        // no stamp and has no earlier registration, so he resolves through the entry deduction — which
        // is the right answer for an intake and an honest inference for a transfer, marked as such.
        await stamper.StampAsync(registrations, ct);

        MoveReorientedStamps(registrations, existing, reoriented);

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A réorientation is the second act, besides an effectivity rule, allowed to move a
    /// <b>confirmed</b> student stamp.
    /// </summary>
    /// <remarks>
    /// <para>The objection <c>CnpnTargeting</c> raises — never re-evaluate an existing student's stamp
    /// — is about a rule re-selecting a population every September. This is neither: the faculty has
    /// moved one named student from one programme to another, and a <c>CnpnVersion</c> belongs to
    /// exactly one programme, so leaving the old stamp in place would have <c>Student.CnpnVersionId</c>
    /// naming a text that governs a cursus he has left. Everything reading <c>TotalYears</c> from it —
    /// the final-year gate, the déliberation's « est-ce sa dernière année ? » — would then answer from
    /// the wrong arrêté.</para>
    /// <para>The new stamp is the one his own registration just resolved, so the student and his
    /// registration cannot disagree.</para>
    /// </remarks>
    private static void MoveReorientedStamps(
        IReadOnlyList<Registration> registrations,
        IReadOnlyDictionary<Guid, Student> students,
        IReadOnlyList<Guid> reoriented)
    {
        if (reoriented.Count == 0) return;

        var moved = reoriented.ToHashSet();

        foreach (var registration in registrations)
        {
            if (!moved.Contains(registration.StudentId)) continue;
            if (!students.TryGetValue(registration.StudentId, out var student)) continue;

            // ⚠ Unresolved is not "leave it as it was". The stamper could find no text of the new
            // programme for this student — PGSH holds none applying at or before his entry — and the
            // one he arrived with governs a cursus he has left. Keeping it would make TotalYears, and
            // therefore how many years he owes, answer from the wrong arrêté; null says « never
            // resolved », which is exactly what is true.
            if (registration.CnpnVersionId is not { } stamp)
            {
                student.ClearCnpnVersion();
                continue;
            }

            student.AssignCnpnVersion(stamp, isInferred: false, overrideExisting: true);
        }
    }

    private async Task<Dictionary<Guid, Student>> TrackedStudentsAsync(
        IReadOnlyList<RowDraft> work, CancellationToken ct)
    {
        var ids = work
            .Where(d => !d.CreatesStudent && d.Student is not null)
            .Select(d => d.Student!.Id)
            .Distinct()
            .ToList();

        if (ids.Count == 0) return [];

        return await dbContext.Students
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
    }

    /// <summary>
    /// A brand-new graph, so the key may be set here: <c>Add</c> marks the whole graph <c>Added</c>
    /// whatever its key values. The rule that bites is the other one — never pre-set a store-generated
    /// key on a child added to an <em>already-tracked</em> parent.
    /// </summary>
    private static Student BuildStudent(RowDraft draft, AcademicProgram programme)
    {
        var fields = draft.Fields!;
        var row = draft.Row;

        return new Student
        {
            Id = Guid.NewGuid(),
            FirstName = row.FirstName?.Trim() ?? "",
            LastName = row.LastName?.Trim() ?? "",
            Email = draft.GeneratedEmail ?? row.Email!.Trim(),
            CNE = draft.GeneratedCne ?? row.Cne!.Trim(),
            Appogee = draft.GeneratedAppogee ?? row.Appogee!.Trim(),
            CIN = row.Cin?.Trim(),
            Gender = fields.Gender,
            DateOfBirth = fields.DateOfBirth,
            PlaceOfBirth = fields.PlaceOfBirth,
            BacYear = fields.BacYear,
            BacSeries = fields.BacSeries,
            AgreementType = fields.Agreement,
            // The programme is the level's, never a column: a student inscribed into a Pharmacie year
            // is a pharmacy student, and a cell disagreeing with that has no defensible winner.
            AcademicProgram = programme,
            // Left at the entity's own default when the file does not state it, rather than invented.
            AccessGrade = fields.AccessGrade ?? 10.01M,
        };
    }
}
