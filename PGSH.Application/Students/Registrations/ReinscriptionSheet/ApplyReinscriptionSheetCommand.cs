using System.Text.Json;
using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// Applies the faculty's réinscription roll: records the closing year's verdict where the two
/// « Etape » columns carry one, and creates the next year's registration at the level the file names.
/// </summary>
/// <remarks>
/// <para><b>All-or-nothing on the errors, idempotent on everything else.</b> A line that is
/// <em>wrong</em> — a duplicated code, a level disagreeing with the registration on record — refuses
/// the whole file, because the write it would produce is a verdict on somebody's year and nothing
/// puts that back. A line that is merely not actionable — an unknown student, a master's programme,
/// a student already rolled over — is skipped and counted, so the file can be re-sent once the
/// missing students have been inscribed.</para>
///
/// <para>⚠ <b>It also ends the cursus of the final-year students the file does <em>not</em> name.</b>
/// The roll is the list of who is coming back, so an absence in a student's last year means he has
/// defended — recorded « Diplômé », <c>Inferred</c>, so a real defence roll arriving later
/// (<c>Declared</c>) corrects it by itself. An absence anywhere else decides nothing and is left
/// alone.</para>
///
/// <para>⚠ <b>It also creates the students the file names and PGSH has never seen.</b> 26 of the
/// 6 862 lines of the 2026-2027 roll are these. Skipping them was defensible — creating an identity is
/// the inscription's act — and it was still wrong in practice: the only trace was a downloaded
/// spreadsheet, so nobody acted on them. They are created from the Apogée and the name, and flagged
/// <c>IncompleteStudentFile</c>, which is <b>advisory</b>: they partition and plan like everyone else
/// while somebody completes the dossier. Only the e-mail is manufactured, and it is allocated against
/// the addresses in the store because an address is a login.</para>
///
/// <para>⚠ <b>It creates registrations our own record says are not ready, and holds them instead
/// of refusing them.</b> A row whose student is entering the last year of his text still owing an
/// earlier stage used to be skipped; measured on the 2026-2027 roll that silently dropped 182 of the
/// 651 7ᵉ année Médecine the faculty itself named as coming back. The final year is not a year one
/// passes — the student sits in it revalidating stages one at a time, so the re-registration is the
/// mechanism that clears the debt, and in most of those 182 cases the stage was served and only the
/// évaluation is missing. The registration is therefore created and
/// <c>RegistrationHoldReason.OutstandingPriorStages</c> keeps it out of every roster and affectation
/// until scolarité releases it. Every absentee is held too — see
/// <c>ReinscriptionSheetReport.AbsenteesHeld</c>.</para>
///
/// <para>⚠ <b>That is why <see cref="ConfirmedGraduationCount"/> exists</b>, and it did not until
/// graduation did. Every other write lands on a student the file names, so a registration created
/// between the preview and the apply is simply not in it; a « Diplômé » lands on a student it does
/// not name, and a registration created in between would have its cursus ended by a confirmation
/// nobody gave for it. Exactly the case <c>ApplyDeliberationCommand.ConfirmedDefaultCount</c> exists
/// for, and a boolean would not do.</para>
///
/// <para>⚠ <b>Expect the write to take minutes on a real roll.</b> Each verdict raises
/// <c>RegistrationYearOutcomeRecordedDomainEvent</c> and each new registration raises
/// <c>StudentRegisteredDomainEvent</c>, and <c>ApplicationDbContext</c> publishes them after the
/// commit, one at a time — ~12 800 handlers for the 2026-2027 file. The transaction itself is quick;
/// the <c>Histories</c> count climbing afterwards is progress, not a hang.</para>
/// </remarks>
/// <param name="ConfirmedGraduationCount">
/// The number of graduations the preview showed. Sent back rather than re-derived, so a registration
/// created since refuses instead of being graduated silently. Omitted, the apply runs only if the
/// plan finds none.
/// </param>
public sealed record ApplyReinscriptionSheetCommand(
    IReadOnlyList<ReinscriptionSheetRow> Rows,
    int FromAcademicYearId,
    int ToAcademicYearId,
    int? ConfirmedGraduationCount = null) : ICommand<ReinscriptionSheetReport>, IAuditableCommand
{
    public string AuditAction => "REINSCRIPTION_SHEET_APPLIED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => ToAcademicYearId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        fromAcademicYearId = FromAcademicYearId,
        toAcademicYearId = ToAcademicYearId,
        rowCount = Rows.Count,
        confirmedGraduationCount = ConfirmedGraduationCount,
    });
}

internal sealed class ApplyReinscriptionSheetCommandValidator
    : AbstractValidator<ApplyReinscriptionSheetCommand>
{
    public ApplyReinscriptionSheetCommandValidator()
    {
        RuleFor(x => x.FromAcademicYearId).GreaterThan(0);
        RuleFor(x => x.ToAcademicYearId).GreaterThan(0);
    }
}

internal sealed class ApplyReinscriptionSheetCommandHandler(
    IApplicationDbContext dbContext,
    ReinscriptionSheetPlanner planner,
    RegistrationCnpnStamper stamper,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyReinscriptionSheetCommand, ReinscriptionSheetReport>
{
    public async Task<Result<ReinscriptionSheetReport>> Handle(
        ApplyReinscriptionSheetCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ReinscriptionSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReinscriptionSheetReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.FromAcademicYearId, request.ToAcademicYearId, request.Rows, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<ReinscriptionSheetReport>(plan.Error);

        var report = plan.Value.Report;

        // ⚠ Refused before anything is written, and the refusal names the first offending line. The
        // preview carries all of them; this is what somebody who skipped the preview sees.
        if (!report.CanApply)
        {
            var first = report.Rows.First(r => r.Status.IsError());
            return Result.Failure<ReinscriptionSheetReport>(
                ReinscriptionSheetErrors.RowsRefused(report.ErrorCount, first.SheetRow, first.Message));
        }

        // ⚠ Before anything is written. The count is the operator's, not the plan's: re-deriving it
        // here would confirm whatever the plan happens to find, which is the thing being guarded.
        int graduations = plan.Value.Graduations.Count;
        if ((request.ConfirmedGraduationCount ?? 0) != graduations)
            return Result.Failure<ReinscriptionSheetReport>(
                ReinscriptionSheetErrors.GraduationsNotConfirmed(
                    request.ConfirmedGraduationCount ?? 0, graduations));

        var recordedOn = DateTime.UtcNow;
        var created = new List<Registration>(plan.Value.Work.Count);

        foreach (var item in plan.Value.Work)
        {
            // The verdict goes through the aggregate, never through the setter, so the timeline entry
            // and the outcome cannot disagree — the same route the déliberation and the single-row
            // correction both take. Declared, not Inferred: this is the faculty's own document.
            if (item is { Source: not null, Outcome: { } outcome })
            {
                var recorded = item.Source.RecordYearOutcome(
                    outcome, RegistrationOutcomeSource.Declared, null, recordedOn);

                if (recorded.IsFailure)
                    return Result.Failure<ReinscriptionSheetReport>(recorded.Error);
            }

            var registration = new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = item.StudentId,
                AcademicYearId = plan.Value.ToAcademicYearId,
                LevelId = item.ToLevelId,
                // Active, not Pending: nothing filters planning by this field, so a Pending
                // registration would be grouped and planned exactly like an active one while
                // claiming not to be enrolled. Active is also what the year means — in progress.
                Status = RegistrationStatus.Active,
                RegistrationDate = recordedOn,
                // No roster: répartition is AutoArrangeGroupsCommand's job and runs after this, which
                // is what puts these students in the « Non réparti » bucket it reads from.
                AcademicGroupId = null,
            };

            registration.Raise(new StudentRegisteredDomainEvent(
                registration.Id, item.StudentId, item.ToLevelId, plan.Value.ToAcademicYearId));

            // ⚠ Held at birth, in the same unit of work that creates it. The registration exists
            // because the faculty's roll names the student; the hold exists because our own stage
            // record disagrees, and both facts have to land together or there is a window in which
            // an auto-arrange could sweep him into a roster he may not be in. See
            // RegistrationHoldReason.OutstandingPriorStages for why this is a hold and not a refusal.
            if (item.Hold is { } hold)
            {
                var placed = registration.PlaceOnHold(hold.Reason, hold.Evidence, recordedOn);

                if (placed.IsFailure)
                    return Result.Failure<ReinscriptionSheetReport>(placed.Error);
            }

            dbContext.Registrations.Add(registration);
            created.Add(registration);
        }

        // The final-year absentees. Through the aggregate like every other verdict, and Inferred —
        // PGSH read an absence, nobody named them. RecordYearOutcome refuses Inferred over Declared,
        // which is why the planner skips a registration already carrying a verdict rather than
        // discovering the refusal here.
        foreach (var graduation in plan.Value.Graduations)
        {
            var recorded = graduation.Registration.RecordYearOutcome(
                RegistrationStatus.Graduated, RegistrationOutcomeSource.Inferred, null, recordedOn);

            if (recorded.IsFailure)
                return Result.Failure<ReinscriptionSheetReport>(recorded.Error);
        }

        // ── the students the roll names and PGSH does not hold ──────────────────────────────────
        // Created from what the file actually carries — an Apogée and a name — and flagged so the rest
        // of the dossier gets filled in. ⚠ The flag is advisory (IncompleteStudentFile is not in
        // RegistrationHoldReasonExtensions.Blocking): they are cut into rosters and planned like
        // everyone else. A missing date de naissance is not a reason to keep a student out of a
        // rotation; it is a reason to finish his file.
        foreach (var newcomer in plan.Value.NewStudents)
        {
            var student = new Student
            {
                Id = Guid.NewGuid(),
                LastName = newcomer.LastName,
                FirstName = newcomer.FirstName,
                // ⚠ Manufactured, and the only value that is. Users.Email is NOT NULL UNIQUE and
                // SyncUserMiddleware falls back to matching a Keycloak sub on it, so the address was
                // allocated by the planner against the store — never invented here.
                Email = newcomer.GeneratedEmail!,
                // ⚠ No CNE. The row carries an Apogée and Student.CNE is optional since the LEGACY-
                // placeholders were cleared: a manufactured code would read, in every list and every
                // canvas, exactly like one somebody holds.
                CNE = null,
                Appogee = newcomer.Appogee,
                // The programme is the level's, never a guess: a student the file sends into a
                // Pharmacie year is a pharmacy student.
                AcademicProgram = newcomer.Programme,
                // ⚠ Required by the schema and absent from the roll, so it is left empty rather than
                // invented — an invented bac year reads exactly like a recorded one. Emptiness here is
                // precisely what « dossier à compléter » names, and the flag is what sends somebody to
                // fill it.
                BacYear = "",
            };

            var registration = new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                Student = student,
                AcademicYearId = plan.Value.ToAcademicYearId,
                LevelId = newcomer.ToLevelId,
                Status = RegistrationStatus.Active,
                RegistrationDate = recordedOn,
                AcademicGroupId = null,
            };

            var flagged = registration.PlaceOnHold(
                RegistrationHoldReason.IncompleteStudentFile, newcomer.Evidence, recordedOn);

            if (flagged.IsFailure)
                return Result.Failure<ReinscriptionSheetReport>(flagged.Error);

            registration.Raise(new StudentRegisteredDomainEvent(
                registration.Id, student.Id, newcomer.ToLevelId, plan.Value.ToAcademicYearId));

            // ⚠ Add the *registration*, not the student. `Add` marks the whole reachable graph Added,
            // and the graph is only whole from this end: the registration references the student and
            // owns the hold, whereas the student's own Registrations collection was never populated —
            // adding him alone left the registration untracked and nothing was written.
            //
            // Adding the graph as a unit is also what keeps the hold's store-generated key classified
            // Added rather than Modified. See InternshipAssignment.Delocalize for the other half.
            dbContext.Registrations.Add(registration);
            created.Add(registration);
        }

        // Every closing-year registration the roll does not name — the graduations included. The
        // verdict above says what PGSH concluded; the hold says nobody has confirmed it, and keeps
        // the student out of every roster and every affectation until somebody does. Idempotent per
        // reason, so re-running the roll neither stacks flags nor rewrites the evidence somebody is
        // about to act on.
        foreach (var absentee in plan.Value.AbsenteeHolds)
        {
            var placed = absentee.Registration.PlaceOnHold(
                RegistrationHoldReason.AbsentFromReinscriptionRoll, absentee.Evidence, recordedOn);

            if (placed.IsFailure)
                return Result.Failure<ReinscriptionSheetReport>(placed.Error);
        }

        if (created.Count > 0)
        {
            // The rollover is where an effectivity rule authored over the summer actually bites: it is
            // the act that creates next year's registrations, and a repeater re-entering the level a
            // rule names is stamped here rather than by anyone remembering to run a command.
            await stamper.StampAsync(created, cancellationToken);
        }

        // One SaveChanges for the whole roll: the verdicts and the registrations they justify are one
        // unit of work, and a half-applied file is a promotion nobody can tell apart from one somebody
        // meant that way.
        await dbContext.SaveChangesAsync(cancellationToken);

        return report;
    }
}
