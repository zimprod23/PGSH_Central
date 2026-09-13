using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.InternshipAssignments.Sheet;
using PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Walking one application of the canevas back. The claim under test is not « it removes rows » — it is
// that the undo is **total**: what the import destroyed comes back exactly as it stood, cell and flags
// included. That only holds because the import refuses to destroy a mark or a day of attendance, so
// those two guards are tested here as well as on the import.
public class AffectationImportReversalTests
{
    private const int CardioId = 1;
    private const int PneumoId = 2;

    private static readonly DateOnly Before = new(2025, 9, 1);
    private static readonly DateOnly BeforeEnd = new(2025, 9, 30);
    private static readonly DateOnly After = new(2025, 10, 1);
    private static readonly DateOnly AfterEnd = new(2025, 10, 31);

    private sealed record Scenario(Stage Stage, AcademicGroup Group, List<Registration> Students);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, int students = 1)
    {
        var stage = db.SeedCatalog();
        db.SeedService(CardioId, "Cardiologie");
        db.SeedService(PneumoId, "Pneumologie");

        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var roll = new List<Registration>();
        for (int i = 0; i < students; i++)
            roll.Add(db.SeedRegistration($"Etudiant{i}", $"Nom{i}", group));

        await db.SaveChangesAsync();
        return new Scenario(stage, group, roll);
    }

    private static AffectationSheetRow Row(
        Registration r, string service = "Pneumologie", int sheetRow = 2) =>
        new(sheetRow, r.Student.Appogee, r.Student.CNE, "Cardiologie", service,
            null, After, AfterEnd, null);

    private static async Task<Guid> ImportAsync(
        ApplicationDbContext db, params AffectationSheetRow[] rows)
    {
        var (handler, _) = db.AffectationSheetHandler();
        var preview = await db.AffectationSheetPreview().Handle(
            new PreviewAffectationSheetQuery(rows, TestHarness.LevelId), default);

        var applied = await handler.Handle(new ApplyAffectationSheetCommand(
            rows, TestHarness.LevelId,
            preview.Value.Affectations, preview.Value.PeriodsToDrop,
            FileName: "canevas.xlsx"), default);

        applied.IsSuccess.Should().BeTrue();
        return await db.AffectationImports.Select(i => i.Id).SingleAsync();
    }

    // ─── The undo is total ────────────────────────────────────────────────────

    [Fact]
    public async Task Undoing_a_created_affectation_removes_it_whole()
    {
        await using var db = TestHarness.NewContext("undo-created");
        var s = await SeedAsync(db);

        var importId = await ImportAsync(db, Row(s.Students[0]));
        (await db.InternshipAssignments.CountAsync()).Should().Be(1);

        var preview = (await db.ReverseImportPreview().Handle(
            new PreviewAffectationImportReversalQuery(importId), default)).Value;

        preview.AffectationsToRemove.Should().Be(1);
        preview.PeriodsToRestore.Should().Be(0, "there was nothing before it");
        preview.CanApply.Should().BeTrue();

        var (handler, trail) = db.ReverseImportHandler();
        var undone = await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        undone.IsSuccess.Should().BeTrue();
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
        trail.Fields["affectationsRemoved"].Should().Be(1);
    }

    /// <summary>
    /// ⚠ The case the whole design exists for: the import <b>replaced</b> a published rotation, and the
    /// undo has to put it back with its grid cell and its lifecycle flags — not merely a période in the
    /// right service on the right dates. Without the cell, the promotion's plan and its execution
    /// records stay permanently out of agreement and no screen says why.
    /// </summary>
    [Fact]
    public async Task Undoing_a_replacement_restores_the_periods_with_their_cell_and_their_flags()
    {
        await using var db = TestHarness.NewContext("undo-restores");
        var s = await SeedAsync(db);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var assignment = db.SeedAssignment(s.Students[0], cohort);
        var slot = db.SeedSlot(s.Stage, slotId: 1, periodNumber: 1, start: Before, end: BeforeEnd);
        var cell = db.SeedSlotAssignment(1, cohort, slot, db.Services.Local.First(x => x.Id == CardioId));
        var original = db.SeedPeriod(assignment, db.Services.Local.First(x => x.Id == CardioId),
            Before, BeforeEnd, started: true, complete: true);
        original.CohortSlotAssignmentId = cell.Id;
        await db.SaveChangesAsync();

        var importId = await ImportAsync(db, Row(s.Students[0]));

        var afterImport = await db.ServicePeriods.AsNoTracking()
            .SingleAsync(p => p.InternshipAssignmentId == assignment.Id);
        afterImport.ServiceId.Should().Be(PneumoId);
        afterImport.CohortSlotAssignmentId.Should().BeNull("what the sheet writes is hors grille");

        var preview = (await db.ReverseImportPreview().Handle(
            new PreviewAffectationImportReversalQuery(importId), default)).Value;

        preview.AffectationsToRemove.Should().Be(0);
        preview.PeriodsToRestore.Should().Be(1);
        preview.PublishedPeriodsToRestore.Should().Be(1);

        var (handler, trail) = db.ReverseImportHandler();
        await handler.Handle(new ReverseAffectationImportCommand(importId, 0), default);

        var restored = await db.ServicePeriods.AsNoTracking()
            .SingleAsync(p => p.InternshipAssignmentId == assignment.Id);

        restored.ServiceId.Should().Be(CardioId);
        restored.StartDate.Should().Be(Before);
        restored.EndDate.Should().Be(BeforeEnd);
        restored.IsStarted.Should().BeTrue("a rotation that was under way must not come back as a plan");
        restored.IsComplete.Should().BeTrue();
        restored.CohortSlotAssignmentId.Should().Be(cell.Id, "the grid and the périodes agree again");

        trail.Fields["periodsRestored"].Should().Be(1);
    }

    [Fact]
    public async Task An_import_cannot_be_undone_twice()
    {
        await using var db = TestHarness.NewContext("undo-twice");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        var (handler, _) = db.ReverseImportHandler();
        await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        var again = await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("Affectations.ImportAlreadyReversed");
    }

    /// <summary>The record survives its own reversal — « il ne s'est rien passé » is a different answer.</summary>
    [Fact]
    public async Task A_reversed_import_is_kept_and_says_when_it_was_reversed()
    {
        await using var db = TestHarness.NewContext("undo-kept");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        var (handler, _) = db.ReverseImportHandler();
        await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        var kept = await db.AffectationImports.AsNoTracking().SingleAsync(i => i.Id == importId);

        kept.Status.Should().Be(AffectationImportStatus.Reversed);
        kept.ReversedAtUtc.Should().NotBeNull();
        kept.FileName.Should().Be("canevas.xlsx");
        (await db.AffectationImportEntries.CountAsync()).Should().Be(1, "the entries are kept too");
    }

    // ─── What it refuses, and that it wrote nothing ───────────────────────────

    [Fact]
    public async Task An_affectation_evaluated_since_the_import_refuses_the_whole_reversal()
    {
        await using var db = TestHarness.NewContext("undo-evaluated");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        var written = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync();

        var period = written.ServicePeriods.Single();
        written.Start();
        written.CompletePeriod(period.Id);
        written.SubmitEvaluation(period.Id, new ServiceEvaluation { TotalScore = 15m });
        await db.SaveChangesAsync();

        var preview = (await db.ReverseImportPreview().Handle(
            new PreviewAffectationImportReversalQuery(importId), default)).Value;

        preview.ErrorCount.Should().Be(1);
        preview.CanApply.Should().BeFalse();
        preview.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationImportReversalRowStatus.EvaluatedSince);

        var (handler, _) = db.ReverseImportHandler();
        var refused = await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("AffectationImportReversal.HasChanged");
        (await db.InternshipAssignments.CountAsync()).Should().Be(1, "a refusal writes nothing");
        (await db.ServiceEvaluation.CountAsync()).Should().Be(1, "least of all the mark");
    }

    [Fact]
    public async Task A_rotation_republished_since_the_import_refuses_the_reversal()
    {
        await using var db = TestHarness.NewContext("undo-changed");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        // The sheet writes hors grille; a période carrying a cell can only have arrived since.
        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var slot = db.SeedSlot(s.Stage, slotId: 1, periodNumber: 1, start: Before, end: BeforeEnd);
        var cell = db.SeedSlotAssignment(1, cohort, slot, db.Services.Local.First(x => x.Id == CardioId));
        var period = await db.ServicePeriods.SingleAsync();
        period.CohortSlotAssignmentId = cell.Id;
        await db.SaveChangesAsync();

        var preview = (await db.ReverseImportPreview().Handle(
            new PreviewAffectationImportReversalQuery(importId), default)).Value;

        preview.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationImportReversalRowStatus.ChangedSince);
        preview.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task A_stale_confirmed_count_refuses_and_writes_nothing()
    {
        await using var db = TestHarness.NewContext("undo-count");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        var (handler, _) = db.ReverseImportHandler();
        var refused = await handler.Handle(new ReverseAffectationImportCommand(importId, 9), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("AffectationImportReversal.CountMismatch");
        (await db.InternshipAssignments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_affectation_deleted_since_is_counted_and_skipped_rather_than_failing()
    {
        await using var db = TestHarness.NewContext("undo-gone");
        var s = await SeedAsync(db, students: 2);
        var importId = await ImportAsync(db, Row(s.Students[0], sheetRow: 2), Row(s.Students[1], sheetRow: 3));

        var one = await db.InternshipAssignments.Include(a => a.ServicePeriods).FirstAsync();
        db.InternshipAssignments.Remove(one);
        await db.SaveChangesAsync();

        var preview = (await db.ReverseImportPreview().Handle(
            new PreviewAffectationImportReversalQuery(importId), default)).Value;

        preview.AlreadyGone.Should().Be(1);
        preview.ErrorCount.Should().Be(0, "a row with nothing left to undo is not a mistake");
        preview.CanApply.Should().BeTrue();

        var (handler, _) = db.ReverseImportHandler();
        var undone = await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        undone.IsSuccess.Should().BeTrue();
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
    }

    // ─── The aggregate's own guards ───────────────────────────────────────────
    //
    // ⚠ Tested directly, and the reason is worth stating: the planner refuses these cases too, so a
    // test driven through the handler passes whether or not the aggregate guards anything. Measured by
    // breaking the domain guard — every handler test stayed green. The aggregate's refusal is the one
    // that cannot be got round (a future caller, another act), so it earns its own test.

    [Fact]
    public void DeclareRotation_refuses_over_attendance_whatever_the_caller_checked()
    {
        var assignment = WithOnePeriod(attendance: true, evaluated: false);

        var result = assignment.DeclareRotation(TestHarness.StageId,
            [new DeclaredPeriod(PneumoId, After, AfterEnd)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Affectations.DeclaredRotationOverAttendance");
        assignment.ServicePeriods.Should().ContainSingle().Which.ServiceId.Should().Be(CardioId);
    }

    [Fact]
    public void RestoreRotation_refuses_over_attendance_arrived_since()
    {
        var assignment = WithOnePeriod(attendance: true, evaluated: false);

        var result = assignment.RestoreRotation(TestHarness.StageId,
            [new RestoredPeriod(CardioId, Before, BeforeEnd, true, true, false, false, false, null, null)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Affectations.RestoredRotationOverAttendance");
    }

    [Fact]
    public void RestoreRotation_refuses_over_a_mark_arrived_since()
    {
        var assignment = WithOnePeriod(attendance: false, evaluated: true);

        var result = assignment.RestoreRotation(TestHarness.StageId,
            [new RestoredPeriod(CardioId, Before, BeforeEnd, true, true, false, false, false, null, null)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Affectations.RestoredRotationOverMark");
    }

    /// <summary>The control: with neither, the same call goes through and puts the période back.</summary>
    [Fact]
    public void RestoreRotation_writes_the_period_back_when_nothing_arrived_since()
    {
        var assignment = WithOnePeriod(attendance: false, evaluated: false);

        var result = assignment.RestoreRotation(TestHarness.StageId,
            [new RestoredPeriod(CardioId, Before, BeforeEnd, true, true, false, false, false, 7, null)]);

        result.IsSuccess.Should().BeTrue();
        var restored = assignment.ServicePeriods.Should().ContainSingle().Subject;
        restored.ServiceId.Should().Be(CardioId);
        restored.CohortSlotAssignmentId.Should().Be(7);
        restored.IsStarted.Should().BeTrue();
    }

    /// <summary>An assignment holding one période in Pneumologie, optionally marked or attended.</summary>
    private static InternshipAssignment WithOnePeriod(bool attendance, bool evaluated)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), RegistrationId = Guid.NewGuid() };
        var period = new ServicePeriod
        {
            Id = Guid.NewGuid(), ServiceId = CardioId,
            StartDate = After, EndDate = AfterEnd, IsStarted = true,
        };

        if (attendance)
            period.Attendance.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), ServicePeriodId = period.Id,
                Date = After.AddDays(1), Status = AttendanceStatus.Present,
            });

        if (evaluated)
            period.Evaluation = new ServiceEvaluation { TotalScore = 12m };

        assignment.ServicePeriods.Add(period);
        return assignment;
    }

    [Fact]
    public async Task A_caller_who_is_not_scolarite_is_refused()
    {
        await using var db = TestHarness.NewContext("undo-forbidden");
        var s = await SeedAsync(db);
        var importId = await ImportAsync(db, Row(s.Students[0]));

        var (handler, _) = db.ReverseImportHandler(db.StrangerAuthorizer());
        var refused = await handler.Handle(new ReverseAffectationImportCommand(importId, 1), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("AffectationSheet.NotAllowed");
        (await db.InternshipAssignments.CountAsync()).Should().Be(1);
    }
}
