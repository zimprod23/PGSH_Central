using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.AcademicYears;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Bulk entry of a stage's marks from a spreadsheet. The rules that matter are the ones that stop a
// bad sheet from landing: the preview must see every problem, and one problem anywhere must refuse
// the whole import — a partial grade import is unreconcilable.
public class EvaluationImportTests
{
    private const int ChefServiceId    = 1;
    private const int ForeignServiceId = 2;

    private static readonly Guid ChefIdentity = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(Stage Stage, InternshipAssignment Sara, InternshipAssignment Ali);

    /// <summary>Two students on one stage, each with one closed rotation in the chef's service.</summary>
    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, bool closePeriods = true)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var chefService = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var sara = db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        var ali  = db.SeedAssignment(db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup), cohort);

        db.SeedPeriod(sara, chefService, Start, End);
        db.SeedPeriod(ali,  chefService, Start, End);

        if (closePeriods)
        {
            Close(sara);
            Close(ali);
        }

        await db.SaveChangesAsync();
        return new Scenario(stage, sara, ali);
    }

    /// <summary>
    /// Runs the rotation for real rather than seeding it pre-closed: an assignment whose periods were
    /// simply flagged complete stays Planned, so it could never roll up to Evaluated and the import
    /// would look broken for the wrong reason.
    /// </summary>
    private static void Close(InternshipAssignment assignment)
    {
        assignment.Start().IsSuccess.Should().BeTrue();
        foreach (var period in assignment.ServicePeriods.ToList())
            assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
    }

    private static EvaluationImportPlanner Planner(ApplicationDbContext db, params string[] roles) =>
        new(db, new AcademicYearResolver(db),
            new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity, roles)));

    private static ImportEvaluationsCommandHandler ApplyHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, Planner(db, roles), new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity, roles)));

    private static PreviewEvaluationImportQueryHandler PreviewHandler(ApplicationDbContext db, params string[] roles) =>
        new(Planner(db, roles));

    private static EvaluationImportRow Mark(int sheetRow, string cne, decimal mark) =>
        new(sheetRow, cne, null, null, null, mark, null);

    private static EvaluationImportRow Verdict(int sheetRow, string cne, string verdict) =>
        new(sheetRow, cne, null, null, verdict, null, null);

    private static string CneOf(InternshipAssignment a) => a.Registration.Student.CNE;

    private static ImportEvaluationsCommand Apply(
        Scenario s, EvaluationMode mode, params EvaluationImportRow[] rows) =>
        new(s.Stage.Id, EvaluationImportScope.WholeStage, null, mode, rows);

    private static PreviewEvaluationImportQuery Preview(
        Scenario s, EvaluationMode mode, params EvaluationImportRow[] rows) =>
        new(s.Stage.Id, EvaluationImportScope.WholeStage, null, mode, rows);

    // ─── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_clean_sheet_previews_as_applicable_without_writing_anything()
    {
        await using var db = TestHarness.NewContext("import-preview-clean");
        var s = await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m), Mark(3, CneOf(s.Ali), 11m)),
            default);

        report.IsSuccess.Should().BeTrue();
        report.Value.CanApply.Should().BeTrue();
        report.Value.WillCreate.Should().Be(2);
        report.Value.ErrorCount.Should().Be(0);
        (await db.ServiceEvaluation.CountAsync()).Should().Be(0, "a dry run never writes");
    }

    [Fact]
    public async Task An_unknown_identifier_is_reported_against_its_own_row()
    {
        await using var db = TestHarness.NewContext("import-preview-unknown");
        var s = await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m), Mark(3, "CNE-INEXISTANT", 12m)),
            default);

        report.Value.CanApply.Should().BeFalse();
        report.Value.ErrorCount.Should().Be(1);
        var bad = report.Value.Rows.Single(r => r.SheetRow == 3);
        bad.Status.Should().Be(EvaluationImportRowStatus.UnknownStudent);
        bad.Cne.Should().Be("CNE-INEXISTANT",
            "the report echoes the identifier as typed, not the normalized lookup key");
        report.Value.Rows.Single(r => r.SheetRow == 2).Status
            .Should().Be(EvaluationImportRowStatus.WillCreate, "the other rows are still reported normally");
    }

    [Fact]
    public async Task A_student_listed_twice_is_refused_rather_than_graded_twice()
    {
        await using var db = TestHarness.NewContext("import-preview-duplicate");
        var s = await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m), Mark(3, CneOf(s.Sara), 8m)),
            default);

        report.Value.CanApply.Should().BeFalse();
        report.Value.Rows.Single(r => r.SheetRow == 3).Status
            .Should().Be(EvaluationImportRowStatus.DuplicateStudent);
    }

    [Fact]
    public async Task A_rotation_that_is_still_running_cannot_be_graded()
    {
        await using var db = TestHarness.NewContext("import-preview-open");
        var s = await SeedAsync(db, closePeriods: false);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m)), default);

        report.Value.CanApply.Should().BeFalse();
        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.PeriodNotClosed);
    }

    [Fact]
    public async Task A_mark_outside_the_scale_is_refused()
    {
        await using var db = TestHarness.NewContext("import-preview-range");
        var s = await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 24m)), default);

        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.InvalidValue);
    }

    [Fact]
    public async Task A_row_with_no_value_for_the_chosen_mode_is_refused()
    {
        await using var db = TestHarness.NewContext("import-preview-missing");
        var s = await SeedAsync(db);

        // Numeric import, but the marker filled the verdict column instead.
        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Verdict(2, CneOf(s.Sara), "Validé")), default);

        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.MissingValue);
    }

    [Theory]
    [InlineData("Validé", EvaluationOutcome.Validated)]
    [InlineData("valide", EvaluationOutcome.Validated)]
    [InlineData("VALIDÉ", EvaluationOutcome.Validated)]
    [InlineData("Non validé", EvaluationOutcome.NotValidated)]
    [InlineData("non valide", EvaluationOutcome.NotValidated)]
    public async Task The_verdict_column_is_read_whatever_the_accents_and_casing(
        string typed, EvaluationOutcome expected)
    {
        await using var db = TestHarness.NewContext($"import-verdict-{typed}");
        var s = await SeedAsync(db);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.ValidatePeriod, Verdict(2, CneOf(s.Sara), typed)), default);

        result.IsSuccess.Should().BeTrue();
        (await db.ServiceEvaluation.SingleAsync()).Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task An_unrecognisable_verdict_is_refused_rather_than_guessed()
    {
        await using var db = TestHarness.NewContext("import-verdict-unknown");
        var s = await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            new PreviewEvaluationImportQuery(s.Stage.Id, EvaluationImportScope.WholeStage, null,
                EvaluationMode.ValidatePeriod, [Verdict(2, CneOf(s.Sara), "peut-être")]),
            default);

        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.InvalidValue);
    }

    [Fact]
    public async Task A_ratified_stage_is_reported_as_no_longer_modifiable()
    {
        await using var db = TestHarness.NewContext("import-preview-ratified");
        var s = await SeedAsync(db, closePeriods: false);
        s.Sara.Start().IsSuccess.Should().BeTrue();
        var period = s.Sara.ServicePeriods.Single();
        s.Sara.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
        s.Sara.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 15m,
        }).IsSuccess.Should().BeTrue();
        s.Sara.Validate().IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 18m)), default);

        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.AlreadyRatified);
    }

    [Fact]
    public async Task A_chef_may_not_import_marks_for_a_service_he_does_not_lead()
    {
        await using var db = TestHarness.NewContext("import-preview-foreign");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        db.SeedService(ChefServiceId, "Cardiologie", chef);
        var foreign = db.SeedService(ForeignServiceId, "Réanimation");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var sara = db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        db.SeedPeriod(sara, foreign, Start, End);
        Close(sara);
        await db.SaveChangesAsync();
        var s = new Scenario(stage, sara, sara);

        var report = await PreviewHandler(db).Handle(
            Preview(s, EvaluationMode.Numeric, Mark(2, CneOf(sara), 14m)), default);

        report.Value.Rows.Single().Status.Should().Be(EvaluationImportRowStatus.NotAllowed);
    }

    [Fact]
    public async Task An_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("import-stage-missing");
        await SeedAsync(db);

        var report = await PreviewHandler(db, Roles.Scolarite).Handle(
            new PreviewEvaluationImportQuery(999, EvaluationImportScope.WholeStage, null,
                EvaluationMode.Numeric, [Mark(2, "X", 12m)]),
            default);

        report.IsFailure.Should().BeTrue();
        report.Error.Should().Be(StageErrors.NotFound(999));
    }

    // ─── Apply ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Applying_a_clean_sheet_records_every_mark_through_the_aggregate()
    {
        await using var db = TestHarness.NewContext("import-apply-clean");
        var s = await SeedAsync(db);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m), Mark(3, CneOf(s.Ali), 11m)),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillCreate.Should().Be(2);

        var sara = await db.InternshipAssignments.FirstAsync(a => a.Id == s.Sara.Id);
        sara.FinalScore.Should().Be(14m, "the stage note is recomputed, not just the evaluation stored");
        sara.Result.Should().Be(StageAssignmentResult.Validé);
        sara.Status.Should().Be(InternshipStatus.Evaluated, "the lifecycle rolls up as for a chef's entry");
    }

    [Fact]
    public async Task One_bad_row_refuses_the_entire_import()
    {
        await using var db = TestHarness.NewContext("import-apply-allornothing");
        var s = await SeedAsync(db);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 14m), Mark(3, "INCONNU", 11m)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ImportRejected(1));
        (await db.ServiceEvaluation.CountAsync())
            .Should().Be(0, "the valid row must not land on its own — a partial grade import is unreconcilable");
    }

    [Fact]
    public async Task Re_importing_replaces_the_mark_already_on_record()
    {
        await using var db = TestHarness.NewContext("import-apply-overwrite");
        var s = await SeedAsync(db);
        await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 9m)), default);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.Numeric, Mark(2, CneOf(s.Sara), 15m)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillOverwrite.Should().Be(1);
        (await db.ServiceEvaluation.SingleAsync()).TotalScore.Should().Be(15m);
        (await db.InternshipAssignments.FirstAsync(a => a.Id == s.Sara.Id)).FinalScore.Should().Be(15m);
    }

    [Fact]
    public async Task A_whole_stage_row_grades_every_rotation_of_that_student()
    {
        await using var db = TestHarness.NewContext("import-apply-wholestage");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var first = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var second = db.SeedService(ForeignServiceId, "Réanimation", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var sara = db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        db.SeedPeriod(sara, first, Start, End);
        db.SeedPeriod(sara, second, Start.AddMonths(1), End.AddMonths(1));
        Close(sara);
        await db.SaveChangesAsync();
        var s = new Scenario(stage, sara, sara);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            Apply(s, EvaluationMode.ValidatePeriod, Verdict(2, CneOf(sara), "Validé")), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodCount.Should().Be(2);
        (await db.ServiceEvaluation.CountAsync()).Should().Be(2);
        (await db.InternshipAssignments.FirstAsync(a => a.Id == sara.Id))
            .Result.Should().Be(StageAssignmentResult.Validé);
    }

    [Fact]
    public async Task A_single_period_import_touches_only_the_named_rotation()
    {
        await using var db = TestHarness.NewContext("import-apply-oneperiod");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var first = db.SeedService(ChefServiceId, "Cardiologie", chef);
        var second = db.SeedService(ForeignServiceId, "Réanimation", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var sara = db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);

        var slot1 = db.SeedSlot(stage, slotId: 1, periodNumber: 1, Start, End);
        var slot2 = db.SeedSlot(stage, slotId: 2, periodNumber: 2, Start.AddMonths(1), End.AddMonths(1));
        var cell1 = db.SeedSlotAssignment(1, cohort, slot1, first);
        var cell2 = db.SeedSlotAssignment(2, cohort, slot2, second);
        var p1 = db.SeedPeriod(sara, first, Start, End);
        var p2 = db.SeedPeriod(sara, second, Start.AddMonths(1), End.AddMonths(1));
        p1.CohortSlotAssignment = cell1;
        p2.CohortSlotAssignment = cell2;
        Close(sara);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            new ImportEvaluationsCommand(stage.Id, EvaluationImportScope.SinglePeriod, 2,
                EvaluationMode.Numeric, [Mark(2, CneOf(sara), 16m)]),
            default);

        result.IsSuccess.Should().BeTrue();
        var evaluations = await db.ServiceEvaluation.ToListAsync();
        evaluations.Should().ContainSingle().Which.ServicePeriodId.Should().Be(p2.Id);
    }

    [Fact]
    public async Task A_period_the_stage_does_not_have_is_rejected_before_any_row_is_read()
    {
        await using var db = TestHarness.NewContext("import-badperiod");
        var s = await SeedAsync(db);

        var result = await ApplyHandler(db, Roles.Scolarite).Handle(
            new ImportEvaluationsCommand(s.Stage.Id, EvaluationImportScope.SinglePeriod, 7,
                EvaluationMode.Numeric, [Mark(2, CneOf(s.Sara), 16m)]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ImportPeriodNotInStage(7, s.Stage.Id));
    }

    // ─── Validator ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_by_objective_is_not_importable()
    {
        var result = new ImportEvaluationsCommandValidator().Validate(new ImportEvaluationsCommand(
            1, EvaluationImportScope.WholeStage, null, EvaluationMode.ValidateObjectives,
            [new EvaluationImportRow(2, "X", null, null, null, 12m, null)]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_per_period_import_must_name_its_period()
    {
        var result = new ImportEvaluationsCommandValidator().Validate(new ImportEvaluationsCommand(
            1, EvaluationImportScope.SinglePeriod, null, EvaluationMode.Numeric,
            [new EvaluationImportRow(2, "X", null, null, null, 12m, null)]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_empty_sheet_is_rejected()
    {
        var result = new ImportEvaluationsCommandValidator().Validate(new ImportEvaluationsCommand(
            1, EvaluationImportScope.WholeStage, null, EvaluationMode.Numeric, []));

        result.IsValid.Should().BeFalse();
    }
}
