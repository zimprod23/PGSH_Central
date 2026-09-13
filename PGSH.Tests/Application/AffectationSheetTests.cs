using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.InternshipAssignments.Sheet;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Planning a promotion from an uploaded canevas. The preview and the apply run the same planner, so
// every case is asserted on the preview and — where it writes, or refuses to — on the store
// afterwards. ⚠ A guard ordered after the write returns the same Result.Failure and passes a handler
// test; the refusals here therefore assert the refusal *and* that nothing moved.
public class AffectationSheetTests
{
    private const int CardioId = 1;
    private const int PneumoId = 2;
    private const int ExternalId = 60;
    private const int OtherStageId = 77;

    private static readonly DateOnly Start = new(2025, 10, 1);
    private static readonly DateOnly End = new(2025, 10, 31);

    private sealed record Scenario(Stage Stage, AcademicGroup Group, List<Registration> Students);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, int students = 2)
    {
        var stage = db.SeedCatalog();
        db.SeedService(CardioId, "Cardiologie");
        db.SeedService(PneumoId, "Pneumologie");
        db.SeedService(ExternalId, "Hôpital de Kénitra", isExternal: true);

        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var roll = new List<Registration>();

        for (int i = 0; i < students; i++)
            roll.Add(db.SeedRegistration($"Etudiant{i}", $"Nom{i}", group));

        await db.SaveChangesAsync();
        return new Scenario(stage, group, roll);
    }

    private static AffectationSheetRow Row(
        Registration registration,
        string stage = "Cardiologie",
        string service = "Cardiologie",
        DateOnly? start = null,
        DateOnly? end = null,
        string? reason = null,
        int sheetRow = 2) =>
        new(sheetRow, registration.Student.Appogee, registration.Student.CNE, stage, service,
            HospitalName: null, start ?? Start, end ?? End, reason);

    private static async Task<AffectationSheetReport> PreviewAsync(
        ApplicationDbContext db, params AffectationSheetRow[] rows) =>
        (await db.AffectationSheetPreview().Handle(
            new PreviewAffectationSheetQuery(rows, TestHarness.LevelId), default)).Value;

    // ─── The happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_line_plans_a_student_who_had_nothing()
    {
        await using var db = TestHarness.NewContext("sheet-create");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0]));

        report.WillCreate.Should().Be(1);
        report.Affectations.Should().Be(1);
        report.PeriodsToWrite.Should().Be(1);
        report.CohortsToCreate.Should().Be(1, "the roster has never done this stage");
        report.CanApply.Should().BeTrue();

        var (handler, trail) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([Row(s.Students[0])], TestHarness.LevelId, 1, 0), default);

        applied.IsSuccess.Should().BeTrue();

        var written = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.RegistrationId == s.Students[0].Id);

        written.ServicePeriods.Should().ContainSingle()
            .Which.Should().Match<ServicePeriod>(p =>
                p.ServiceId == CardioId && p.StartDate == Start && p.EndDate == End);

        written.Status.Should().Be(InternshipStatus.Planned, "a declared rotation is a plan, not a fact");
        written.ServicePeriods.Single().CohortSlotAssignmentId.Should()
            .BeNull("nothing in the grid produced it");

        trail.Fields["affectationsCreated"].Should().Be(1);
        trail.Fields["periodsDropped"].Should().Be(0);
    }

    [Fact]
    public async Task Several_lines_of_one_stage_become_one_affectation_with_several_periods()
    {
        await using var db = TestHarness.NewContext("sheet-rotation");
        var s = await SeedAsync(db, students: 1);

        AffectationSheetRow[] rows =
        [
            Row(s.Students[0], service: "Cardiologie", sheetRow: 2),
            Row(s.Students[0], service: "Pneumologie",
                start: Start.AddDays(31), end: End.AddDays(31), sheetRow: 3),
        ];

        var report = await PreviewAsync(db, rows);

        report.TotalRows.Should().Be(2);
        report.Affectations.Should().Be(1, "one stage, two services, one attempt");
        report.PeriodsToWrite.Should().Be(2);

        var (handler, _) = db.AffectationSheetHandler();
        await handler.Handle(new ApplyAffectationSheetCommand(rows, TestHarness.LevelId, 1, 0), default);

        var written = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.RegistrationId == s.Students[0].Id);

        written.ServicePeriods.Select(p => p.ServiceId).Should().BeEquivalentTo([CardioId, PneumoId]);
    }

    [Fact]
    public async Task An_external_service_with_a_motif_is_recorded_as_a_delocalisation()
    {
        await using var db = TestHarness.NewContext("sheet-deloc");
        var s = await SeedAsync(db, students: 1);

        var row = Row(s.Students[0], service: "Hôpital de Kénitra", reason: "Stage servi à Kénitra");
        var report = await PreviewAsync(db, row);

        report.WillDelocalize.Should().Be(1);

        var (handler, trail) = db.AffectationSheetHandler();
        await handler.Handle(new ApplyAffectationSheetCommand([row], TestHarness.LevelId, 1, 0), default);

        var written = await db.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Delocalization)
            .SingleAsync(a => a.RegistrationId == s.Students[0].Id);

        var period = written.ServicePeriods.Should().ContainSingle().Subject;
        period.IsDelocalized.Should().BeTrue();
        period.Delocalization!.Reason.Should().Be("Stage servi à Kénitra");

        // Served before it is recorded — that is what makes it evaluable from the paper fiche.
        period.IsStarted.Should().BeTrue();
        period.IsComplete.Should().BeTrue();
        written.Status.Should().Be(InternshipStatus.Completed);

        trail.Fields["delocalizations"].Should().Be(1);
    }

    // ─── Replacing, and what it costs ─────────────────────────────────────────

    [Fact]
    public async Task An_existing_rotation_is_rebuilt_and_the_cost_is_counted_before_it_is_paid()
    {
        await using var db = TestHarness.NewContext("sheet-replace");
        var s = await SeedAsync(db, students: 1);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var assignment = db.SeedAssignment(s.Students[0], cohort);
        db.SeedPeriod(assignment, db.Services.Local.First(x => x.Id == CardioId),
            new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), started: false);
        await db.SaveChangesAsync();

        var row = Row(s.Students[0], service: "Pneumologie");
        var report = await PreviewAsync(db, row);

        report.WillReplace.Should().Be(1);
        report.PeriodsToDrop.Should().Be(1);
        report.PublishedPeriodsToDrop.Should().Be(0, "the old période was ad-hoc too");
        report.CohortsToCreate.Should().Be(0, "the cohorte already exists");

        var (handler, trail) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([row], TestHarness.LevelId, 1, 1), default);

        applied.IsSuccess.Should().BeTrue();

        var written = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.Id == assignment.Id);

        written.ServicePeriods.Should().ContainSingle()
            .Which.ServiceId.Should().Be(PneumoId, "the sheet replaced the rotation, it did not add to it");

        trail.Fields["periodsDropped"].Should().Be(1);
        trail.Fields["affectationsRebuilt"].Should().Be(1);
    }

    [Fact]
    public async Task A_published_rotation_about_to_be_overwritten_is_counted_apart()
    {
        await using var db = TestHarness.NewContext("sheet-published");
        var s = await SeedAsync(db, students: 1);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var assignment = db.SeedAssignment(s.Students[0], cohort);
        var slot = db.SeedSlot(s.Stage, slotId: 1, periodNumber: 1,
            start: new DateOnly(2025, 9, 1), end: new DateOnly(2025, 9, 30));
        var cell = db.SeedSlotAssignment(1, cohort, slot, db.Services.Local.First(x => x.Id == CardioId));
        var period = db.SeedPeriod(assignment, db.Services.Local.First(x => x.Id == CardioId),
            new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), started: false);
        period.CohortSlotAssignmentId = cell.Id;
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(s.Students[0], service: "Pneumologie"));

        report.PeriodsToDrop.Should().Be(1);
        report.PublishedPeriodsToDrop.Should().Be(1);
        report.Notes.Should().Contain(n => n.Contains("grille") && n.Contains("republier"));
    }

    [Fact]
    public async Task A_file_that_says_what_is_already_on_record_writes_nothing()
    {
        await using var db = TestHarness.NewContext("sheet-unchanged");
        var s = await SeedAsync(db, students: 1);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var assignment = db.SeedAssignment(s.Students[0], cohort);
        db.SeedPeriod(assignment, db.Services.Local.First(x => x.Id == CardioId),
            Start, End, started: false);
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(s.Students[0]));

        report.Unchanged.Should().Be(1);
        report.Affectations.Should().Be(0);
        report.PeriodsToDrop.Should().Be(0);
        report.CanApply.Should().BeTrue("a corrected file must stay re-sendable once it is applied");

        var (handler, _) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([Row(s.Students[0])], TestHarness.LevelId, 0, 0), default);

        applied.IsSuccess.Should().BeTrue();
        (await db.ServicePeriods.CountAsync()).Should().Be(1, "nothing was written and nothing destroyed");
    }

    // ─── What it refuses, and that it wrote nothing ───────────────────────────

    [Fact]
    public async Task A_marked_stage_is_never_overwritten()
    {
        await using var db = TestHarness.NewContext("sheet-marked");
        var s = await SeedAsync(db, students: 1);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        db.SeedGradedAssignment(s.Students[0], cohort,
            db.Services.Local.First(x => x.Id == CardioId), mark: 14m);
        await db.SaveChangesAsync();

        var row = Row(s.Students[0], service: "Pneumologie");
        var report = await PreviewAsync(db, row);

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.AlreadyMarked);
        report.CanApply.Should().BeFalse();

        var (handler, _) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([row], TestHarness.LevelId, 0, 0), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("AffectationSheet.HasErrors");

        var untouched = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.RegistrationId == s.Students[0].Id);

        untouched.ServicePeriods.Should().ContainSingle().Which.ServiceId.Should().Be(CardioId);
        untouched.FinalScore.Should().Be(14m);
    }

    [Fact]
    public async Task One_bad_line_refuses_the_whole_file()
    {
        await using var db = TestHarness.NewContext("sheet-all-or-nothing");
        var s = await SeedAsync(db, students: 2);

        AffectationSheetRow[] rows =
        [
            Row(s.Students[0], sheetRow: 2),
            Row(s.Students[1], service: "Service qui n'existe pas", sheetRow: 3),
        ];

        var report = await PreviewAsync(db, rows);

        report.ErrorCount.Should().Be(1);
        report.CanApply.Should().BeFalse();

        var (handler, _) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand(rows, TestHarness.LevelId, 1, 0), default);

        applied.IsFailure.Should().BeTrue();

        (await db.InternshipAssignments.CountAsync()).Should()
            .Be(0, "the good line must not land while the bad one is unresolved");
        (await db.Cohorts.CountAsync()).Should().Be(0, "nor the cohorte it would have needed");
    }

    [Fact]
    public async Task A_held_registration_is_refused_rather_than_planned_by_spreadsheet()
    {
        await using var db = TestHarness.NewContext("sheet-hold");
        var s = await SeedAsync(db, students: 1);

        db.RegistrationHolds.Add(new RegistrationHold
        {
            Id = Guid.NewGuid(),
            RegistrationId = s.Students[0].Id,
            Reason = RegistrationHoldReason.OutstandingPriorStages,
            Evidence = "Stages antérieurs non validés",
            RaisedOn = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(s.Students[0]));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.OnHold);
        report.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task A_student_in_no_roster_is_refused_because_there_is_no_cohorte_to_join()
    {
        await using var db = TestHarness.NewContext("sheet-no-roster");
        await SeedAsync(db, students: 1);

        var loose = db.SeedRegistration("Sans", "Groupe");
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(loose));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.NoRoster);
    }

    /// <summary>
    /// ⚠ <b>Measured on the live base 13/09/2026, and it refused a file nobody had edited.</b> The 4ᵉ
    /// année Pharmacie holds 232 inscriptions and <b>no roster at all</b> — an un-cut promotion is the
    /// ordinary state — so asking « est-il dans un groupe ? » before « cette ligne dit-elle quelque
    /// chose ? » turned the untouched canvas into 232 errors. A line that asks for nothing cannot be
    /// wrong, whoever it names.
    /// </summary>
    [Fact]
    public async Task A_blank_line_is_not_planned_even_for_a_student_no_rule_would_let_through()
    {
        await using var db = TestHarness.NewContext("sheet-blank-beats-refusals");
        var s = await SeedAsync(db, students: 1);

        var loose = db.SeedRegistration("Sans", "Groupe");
        db.RegistrationHolds.Add(new RegistrationHold
        {
            Id = Guid.NewGuid(),
            RegistrationId = s.Students[0].Id,
            Reason = RegistrationHoldReason.OutstandingPriorStages,
            Evidence = "Stages antérieurs non validés",
            RaisedOn = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var blank = Row(loose) with { ServiceName = null, StartDate = null, EndDate = null };
        var held = Row(s.Students[0]) with { ServiceName = null, StartDate = null, EndDate = null };

        var report = await PreviewAsync(db, blank, held with { SheetRow = 3 });

        report.NotPlanned.Should().Be(2);
        report.ErrorCount.Should().Be(0, "neither line asks for anything");
        report.CanApply.Should().BeTrue();

        // The control: fill the same two lines and both refusals come back.
        var filled = await PreviewAsync(db, Row(loose), Row(s.Students[0], sheetRow: 3));

        filled.ErrorCount.Should().Be(2);
        filled.Rows.Should().Contain(r => r.Status == AffectationSheetRowStatus.NoRoster);
        filled.Rows.Should().Contain(r => r.Status == AffectationSheetRowStatus.OnHold);
    }

    [Fact]
    public async Task An_external_service_without_a_motif_is_refused()
    {
        await using var db = TestHarness.NewContext("sheet-deloc-no-motif");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0], service: "Hôpital de Kénitra"));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.DelocalizationWithoutReason);
    }

    [Fact]
    public async Task A_motif_on_an_in_faculty_service_is_refused_too()
    {
        await using var db = TestHarness.NewContext("sheet-motif-internal");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0], reason: "Parce que"));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.DelocalizationWithoutReason);
    }

    [Fact]
    public async Task A_delocalisation_spread_over_two_lines_is_refused_rather_than_half_written()
    {
        await using var db = TestHarness.NewContext("sheet-deloc-two-lines");
        var s = await SeedAsync(db, students: 1);

        AffectationSheetRow[] rows =
        [
            Row(s.Students[0], service: "Hôpital de Kénitra", reason: "Kénitra", sheetRow: 2),
            Row(s.Students[0], service: "Cardiologie",
                start: Start.AddDays(31), end: End.AddDays(31), sheetRow: 3),
        ];

        var report = await PreviewAsync(db, rows);

        report.Rows.Should().OnlyContain(r =>
            r.Status == AffectationSheetRowStatus.MalformedDelocalization);
    }

    [Fact]
    public async Task Missing_dates_refuse_the_line_they_are_on()
    {
        await using var db = TestHarness.NewContext("sheet-no-dates");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0]) with { StartDate = null });

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.MissingDates);
    }

    [Fact]
    public async Task An_end_before_its_start_is_refused()
    {
        await using var db = TestHarness.NewContext("sheet-bad-order");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0], start: End, end: Start));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.BadDateOrder);
    }

    [Fact]
    public async Task The_same_period_twice_is_refused_rather_than_written_twice()
    {
        await using var db = TestHarness.NewContext("sheet-duplicate");
        var s = await SeedAsync(db, students: 1);

        var report = await PreviewAsync(db, Row(s.Students[0], sheetRow: 2), Row(s.Students[0], sheetRow: 3));

        report.Rows.Should().Contain(r => r.Status == AffectationSheetRowStatus.DuplicateRow);
    }

    [Fact]
    public async Task A_stage_of_another_level_is_not_a_stage_of_this_promotion()
    {
        await using var db = TestHarness.NewContext("sheet-other-level");
        var s = await SeedAsync(db, students: 1);

        db.SeedLevel(levelId: 9, label: "1ère année", year: 1);
        db.Stages.Add(new Stage
        {
            Id = OtherStageId, Name = "Anatomie", LevelId = 9,
            Level = db.Levels.Local.First(l => l.Id == 9), Coefficient = 1,
        });
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(s.Students[0], stage: "Anatomie"));

        report.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(AffectationSheetRowStatus.UnknownStage);
    }

    [Fact]
    public async Task A_student_of_another_promotion_is_told_apart_from_one_who_does_not_exist()
    {
        await using var db = TestHarness.NewContext("sheet-wrong-promotion");
        var s = await SeedAsync(db, students: 1);

        db.SeedLevel(levelId: 9, label: "1ère année", year: 1);
        var elsewhere = db.SeedRegistration("Autre", "Promotion", levelId: 9);
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db,
            Row(elsewhere, sheetRow: 2),
            new AffectationSheetRow(3, "AP-INEXISTANT", null, "Cardiologie", "Cardiologie",
                null, Start, End, null));

        report.Rows.Should().Contain(r => r.Status == AffectationSheetRowStatus.WrongPromotion);
        report.Rows.Should().Contain(r => r.Status == AffectationSheetRowStatus.StudentNotFound);
        _ = s;
    }

    // ─── The two counts ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_plan_that_moved_since_the_apercu_is_refused_on_the_count()
    {
        await using var db = TestHarness.NewContext("sheet-count");
        var s = await SeedAsync(db, students: 1);

        var (handler, _) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([Row(s.Students[0])], TestHarness.LevelId,
                ConfirmedCount: 4, ConfirmedDroppedPeriods: 0), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("AffectationSheet.CountMismatch");
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Destruction_is_confirmed_separately_from_creation()
    {
        await using var db = TestHarness.NewContext("sheet-dropped-count");
        var s = await SeedAsync(db, students: 1);

        var cohort = db.SeedCohortFor(s.Stage, s.Group, cohortId: 500);
        var assignment = db.SeedAssignment(s.Students[0], cohort);
        db.SeedPeriod(assignment, db.Services.Local.First(x => x.Id == CardioId),
            new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), started: false);
        await db.SaveChangesAsync();

        var (handler, _) = db.AffectationSheetHandler();
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([Row(s.Students[0], service: "Pneumologie")],
                TestHarness.LevelId, ConfirmedCount: 1, ConfirmedDroppedPeriods: 0), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("AffectationSheet.DroppedMismatch");

        var untouched = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.Id == assignment.Id);

        untouched.ServicePeriods.Should().ContainSingle().Which.ServiceId.Should().Be(CardioId);
    }

    // ─── What the report says beyond the counts ───────────────────────────────

    [Fact]
    public async Task The_promotion_the_file_does_not_mention_is_named_rather_than_assumed()
    {
        await using var db = TestHarness.NewContext("sheet-not-covered");
        var s = await SeedAsync(db, students: 3);

        var report = await PreviewAsync(db, Row(s.Students[0]));

        report.NotCovered.Should().Be(2);
        report.Notes.Should().Contain(n => n.Contains("nommées nulle part"));
    }

    [Fact]
    public async Task A_stage_the_students_text_does_not_require_is_reported_but_not_refused()
    {
        await using var db = TestHarness.NewContext("sheet-cnpn");
        var s = await SeedAsync(db, students: 1);

        var other = db.SeedStage(PneumoId + 100, "Pneumologie clinique");
        db.SeedCurriculumStage(TestHarness.NewCnpnId, other);
        s.Students[0].StampCnpnVersion(TestHarness.NewCnpnId, RegistrationCnpnSource.Effectivity);
        await db.SaveChangesAsync();

        var report = await PreviewAsync(db, Row(s.Students[0]));

        report.OutsideCnpn.Should().Be(1);
        report.CanApply.Should().BeTrue("the sheet is the human override; the CNPN is reported, not enforced");
        report.Rows.Should().ContainSingle().Which.OutsideCnpn.Should().BeTrue();
    }

    /// <summary>
    /// ⚠ <b>Conditional, and the control is the point.</b> A note that fired on every upload would be
    /// noise, and noise is dismissed — which puts the real ones out of sight. What it warns about is
    /// the only thing the figures cannot show: the transaction is quick and the dossier entries are
    /// written after it, one at a time.
    /// </summary>
    [Fact]
    public async Task A_large_act_says_it_will_take_a_while_and_a_small_one_does_not()
    {
        await using var db = TestHarness.NewContext("sheet-large-act");
        var s = await SeedAsync(db, students: 250);

        var many = s.Students.Select((r, i) => Row(r, sheetRow: i + 2)).ToArray();
        var few = many.Take(5).ToArray();

        var large = await PreviewAsync(db, many);
        var small = await PreviewAsync(db, few);

        large.Affectations.Should().Be(250);
        large.Notes.Should().Contain(n => n.Contains("acte long"));

        small.Affectations.Should().Be(5);
        small.Notes.Should().NotContain(n => n.Contains("acte long"),
            "a warning that fires whatever the data says is noise");
    }

    [Fact]
    public async Task A_caller_who_is_not_scolarite_is_refused()
    {
        await using var db = TestHarness.NewContext("sheet-forbidden");
        var s = await SeedAsync(db, students: 1);

        var (handler, _) = db.AffectationSheetHandler(db.StrangerAuthorizer());
        var applied = await handler.Handle(
            new ApplyAffectationSheetCommand([Row(s.Students[0])], TestHarness.LevelId, 1, 0), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("AffectationSheet.NotAllowed");
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
    }
}
