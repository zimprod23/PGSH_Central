using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Application.Exports;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Stages.Export;
using PGSH.Application.Students.Export;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The two exports, at the handler.
///
/// <para>The workbook is captured rather than rendered: what is under test is <em>what the document
/// says</em> — which population it covers, which cells carry which fact — not whether ClosedXML can
/// write a file. A test that asserted on bytes would pass for the wrong reasons and fail for
/// cosmetic ones.</para>
///
/// <para>⚠ Every guard is a case, and the year rule is the one that matters most here: an export
/// that widens on an omitted year is the évaluation-import defect with a different button on it.</para>
/// </summary>
public class ExportTests
{
    /// <summary>Captures the workbook the handler built instead of turning it into a file.</summary>
    private sealed class CapturingWriter : IExportWorkbookWriter
    {
        public ExportWorkbook? Captured { get; private set; }

        public byte[] Write(ExportWorkbook workbook)
        {
            Captured = workbook;
            return [1, 2, 3];
        }
    }

    private static GetStudentsExportQueryHandler StudentsHandler(
        ApplicationDbContext db, CapturingWriter writer) =>
        new(db, new AcademicYearResolver(db), db.AdminAuthorizer(), writer);

    private static GetStageAssignmentsExportQueryHandler StagesHandler(
        ApplicationDbContext db, CapturingWriter writer) =>
        new(db, new AcademicYearResolver(db), db.AdminAuthorizer(), new WorkingDayProvider(db),
            new ServiceChefProvider(db), writer);

    private static int ColumnOf(ExportSheet sheet, string header) =>
        sheet.Columns.Select((c, i) => (c.Header, i)).First(x => x.Header == header).i;

    private static string? Cell(ExportSheet sheet, int row, string header) =>
        sheet.Rows[row][ColumnOf(sheet, header)].Value;

    private static decimal? Number(ExportSheet sheet, int row, string header) =>
        sheet.Rows[row][ColumnOf(sheet, header)].Number;

    /// <summary>
    /// Seeds a <c>SingleService</c> run: <paramref name="columns"/> consecutive grid cells in one
    /// service, published as the <b>one</b> <c>ServicePeriod</c> spanning them.
    ///
    /// <para>⚠ Coverage rows for <em>every</em> cell, not just the lead. <c>SchedulePublisher</c>
    /// writes them that way, and a fixture that set only <c>CohortSlotAssignmentId</c> would leave
    /// the trailing columns — the whole subject of these cases — invisible.</para>
    /// </summary>
    private static void SeedSingleServiceRun(
        ApplicationDbContext db, InternshipAssignment assignment, Stage stage, Cohort cohort,
        Service service, int columns)
    {
        var period = db.SeedPeriod(
            assignment, service, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1).AddMonths(columns));

        for (int i = 1; i <= columns; i++)
        {
            var slot = db.SeedSlot(
                stage, slotId: 100 + i, periodNumber: i,
                new DateOnly(2026, 1, 1).AddMonths(i - 1), new DateOnly(2026, 1, 28).AddMonths(i - 1));
            var cell = db.SeedSlotAssignment(200 + i, cohort, slot, service);
            db.SeedCoverage(period, cell, leadCell: i == 1);
        }
    }

    // ── Étudiants ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_caller_who_is_not_scolarite_cannot_export_the_roll()
    {
        using var db = TestHarness.NewContext(nameof(A_caller_who_is_not_scolarite_cannot_export_the_roll));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var handler = new GetStudentsExportQueryHandler(
            db, new AcademicYearResolver(db), db.StrangerAuthorizer(), writer);

        var result = await handler.Handle(new GetStudentsExportQuery(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Export.NotAllowed");
        writer.Captured.Should().BeNull("a refused export must not have built the document on its way out");
    }

    /// <summary>
    /// ⚠ The rule the whole application is held to: an omitted year is <b>the current one</b>, never
    /// all of them. A file labelled « liste des étudiants » holding six promotions of history is the
    /// defect, not the convenience.
    /// </summary>
    [Fact]
    public async Task An_omitted_year_exports_the_current_year_only()
    {
        using var db = TestHarness.NewContext(nameof(An_omitted_year_exports_the_current_year_only));
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var group = db.SeedGroup(groupId: 10, groupNumber: 4, rotationGroup: "B");
        db.SeedRegistration("Amina", "Benali", group);
        db.SeedRegistration("Youssef", "Idrissi", academicYearId: TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var sheet = writer.Captured!.Sheets.Single();
        sheet.Rows.Should().HaveCount(1);
        Cell(sheet, 0, "Nom").Should().Be("Benali");
        Cell(sheet, 0, "Groupe").Should().Be("G4");
        Cell(sheet, 0, "Partition").Should().Be("B");
        sheet.Caption.Should().Contain("2025-2026");
    }

    /// <summary>
    /// The answer to « un fichier par promotion, ou une colonne ? » — both. The columns are always
    /// there, and <c>levelId</c> is what cuts the per-promotion file.
    /// </summary>
    [Fact]
    public async Task The_promotion_columns_are_present_and_the_level_filter_cuts_the_promotion_file()
    {
        using var db = TestHarness.NewContext(nameof(The_promotion_columns_are_present_and_the_level_filter_cuts_the_promotion_file));
        db.SeedCatalog();
        db.SeedLevel(levelId: 5, label: "5ème année", year: 5);

        db.SeedRegistration("Amina", "Benali");
        db.SeedRegistration("Sara", "Cherkaoui", levelId: 5);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();

        var all = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);
        all.IsSuccess.Should().BeTrue();
        var everyone = writer.Captured!.Sheets.Single();
        everyone.Rows.Should().HaveCount(2);
        everyone.Columns.Select(c => c.Header).Should().Contain(["Programme", "Niveau"]);
        Cell(everyone, 0, "Niveau").Should().Be("3ème année");
        Cell(everyone, 0, "Programme").Should().Be("Médecine");

        var one = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(LevelId: 5), default);
        one.IsSuccess.Should().BeTrue();
        var promotion = writer.Captured!.Sheets.Single();
        promotion.Rows.Should().HaveCount(1);
        Cell(promotion, 0, "Nom").Should().Be("Cherkaoui");
        promotion.Columns.Select(c => c.Header).Should().Contain("Niveau",
            "the per-promotion file keeps the columns — a row must still say where it came from");
    }

    [Fact]
    public async Task A_level_that_does_not_exist_is_refused_rather_than_silently_exporting_everyone()
    {
        using var db = TestHarness.NewContext(nameof(A_level_that_does_not_exist_is_refused_rather_than_silently_exporting_everyone));
        db.SeedCatalog();
        db.SeedRegistration("Amina", "Benali");
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer)
            .Handle(new GetStudentsExportQuery(LevelId: 4242), default);

        result.IsFailure.Should().BeTrue();
        writer.Captured.Should().BeNull();
    }

    // ── Stages ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_caller_who_is_not_scolarite_cannot_export_the_stage_record()
    {
        using var db = TestHarness.NewContext(nameof(A_caller_who_is_not_scolarite_cannot_export_the_stage_record));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var handler = new GetStageAssignmentsExportQueryHandler(
            db, new AcademicYearResolver(db), db.StrangerAuthorizer(), new WorkingDayProvider(db),
            new ServiceChefProvider(db), writer);

        var result = await handler.Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Export.NotAllowed");
    }

    /// <summary>
    /// The post-validation row, end to end: one attempt driven through the real lifecycle to a
    /// verdict, and the three sheets that come out of it.
    /// </summary>
    [Fact]
    public async Task A_graded_attempt_exports_its_note_its_verdict_and_its_period()
    {
        using var db = TestHarness.NewContext(nameof(A_graded_attempt_exports_its_note_its_verdict_and_its_period));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var workbook = writer.Captured!;
        workbook.Sheets.Select(s => s.Name).Should().Equal("Stages", "Périodes", "Synthèse");

        var stages = workbook.Sheets[0];
        stages.Rows.Should().HaveCount(1);
        Cell(stages, 0, "Nom").Should().Be("Benali");
        Cell(stages, 0, "Stage").Should().Be("Cardiologie");
        Cell(stages, 0, "Service(s)").Should().Be("Service de Cardiologie");
        Cell(stages, 0, "Découpage").Should().Be("Période unique");
        Number(stages, 0, "Nb périodes").Should().Be(1);
        Number(stages, 0, "Note").Should().Be(14.5m);
        Cell(stages, 0, "Résultat").Should().Be("Validé");

        var periods = workbook.Sheets[1];
        periods.Rows.Should().HaveCount(1);
        Number(periods, 0, "Note période").Should().Be(14.5m);
        Cell(periods, 0, "Validée").Should().Be("Oui");
        Cell(periods, 0, "Origine").Should().Be("Hors grille",
            "the seeded période hangs off no cell — imported history, not a published répartition");
        Cell(periods, 0, "Réf. stage").Should().Be(Cell(stages, 0, "Réf. stage"),
            "the two sheets are joined by that key or the detail cannot be read back");

        var summary = workbook.Sheets[2];
        summary.Rows.Should().HaveCount(1);
        Number(summary, 0, "Effectif").Should().Be(1);
        Number(summary, 0, "Validés").Should().Be(1);
        Number(summary, 0, "Taux de validation (%)").Should().Be(100m);
    }

    /// <summary>
    /// The user's own question, at the handler: two périodes, one service, meeting end to end. One
    /// span in the cell — and « Nb périodes » still saying two.
    /// </summary>
    [Fact]
    public async Task Two_contiguous_periods_in_one_service_export_as_one_span_that_still_counts_two()
    {
        using var db = TestHarness.NewContext(nameof(Two_contiguous_periods_in_one_service_export_as_one_span_that_still_counts_two));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        db.SeedPeriod(assignment, service, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        db.SeedPeriod(assignment, service, new DateOnly(2025, 2, 2), new DateOnly(2025, 3, 2));
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var stages = writer.Captured!.Sheets[0];

        Cell(stages, 0, "Période(s)").Should().Be("01/01/2025 – 02/03/2025");
        Number(stages, 0, "Nb périodes").Should().Be(2);
        Number(stages, 0, "Nb services").Should().Be(1);
        Cell(stages, 0, "Découpage").Should().Be("Service unique — 2 périodes contiguës");
        Cell(stages, 0, "Détail des périodes").Should().Contain("P1").And.Contain("P2");

        writer.Captured!.Sheets[1].Rows.Should().HaveCount(2,
            "the détail sheet keeps one row per période whatever the summary collapses");
    }

    [Fact]
    public async Task Two_services_export_as_an_itinerary_and_two_spans()
    {
        using var db = TestHarness.NewContext(nameof(Two_services_export_as_an_itinerary_and_two_spans));
        var stage = db.SeedCatalog();
        var cardio = db.SeedService(1, "Cardiologie");
        var pneumo = db.SeedService(2, "Pneumologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        db.SeedPeriod(assignment, cardio, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));
        db.SeedPeriod(assignment, pneumo, new DateOnly(2025, 2, 2), new DateOnly(2025, 3, 2));
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var stages = writer.Captured!.Sheets[0];

        Cell(stages, 0, "Service(s)").Should().Be("Cardiologie → Pneumologie");
        Cell(stages, 0, "Période(s)").Should().Be("01/01/2025 – 01/02/2025 · 02/02/2025 – 02/03/2025");
        Number(stages, 0, "Nb services").Should().Be(2);
    }

    /// <summary>
    /// ⚠ The year is read from <c>Registration.AcademicYearId</c>, never approximated from the
    /// périodes' dates. A stage that ran into the following September belongs to the year it was
    /// registered in.
    /// </summary>
    [Fact]
    public async Task A_stage_running_past_the_year_end_stays_in_the_year_it_was_registered_in()
    {
        using var db = TestHarness.NewContext(nameof(A_stage_running_past_the_year_end_stays_in_the_year_it_was_registered_in));
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var service = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        // Runs eight days into the next academic year — a date rule would file it under 2026-2027.
        db.SeedPeriod(assignment, service, new DateOnly(2026, 7, 8), new DateOnly(2026, 9, 8));
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        writer.Captured!.Sheets[0].Rows.Should().HaveCount(1);
        Cell(writer.Captured!.Sheets[0], 0, "Année universitaire").Should().Be("2025-2026");
    }

    /// <summary>
    /// The default shows the holes; <c>OnlyEvaluated</c> is the caller saying the file is a PV. Both
    /// are cases because a document that silently dropped the unmarked rows would read as a promotion
    /// nobody planned.
    /// </summary>
    [Fact]
    public async Task Unevaluated_attempts_are_in_the_document_by_default_and_out_of_it_on_request()
    {
        using var db = TestHarness.NewContext(nameof(Unevaluated_attempts_are_in_the_document_by_default_and_out_of_it_on_request));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        var graded = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(graded, cohort, service, mark: 14m);

        var planned = db.SeedRegistration("Youssef", "Idrissi", cohort.AcademicGroup);
        db.SeedAssignment(planned, cohort);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();

        var everything = await StagesHandler(db, writer)
            .Handle(new GetStageAssignmentsExportQuery(), default);
        everything.IsSuccess.Should().BeTrue();
        writer.Captured!.Sheets[0].Rows.Should().HaveCount(2);
        writer.Captured!.Sheets[2].Rows.Should().HaveCount(1);
        Number(writer.Captured!.Sheets[2], 0, "Non évalués").Should().Be(1);

        var pv = await StagesHandler(db, writer)
            .Handle(new GetStageAssignmentsExportQuery(OnlyEvaluated: true), default);
        pv.IsSuccess.Should().BeTrue();
        writer.Captured!.Sheets[0].Rows.Should().HaveCount(1);
        Cell(writer.Captured!.Sheets[0], 0, "Nom").Should().Be("Benali");
    }

    /// <summary>
    /// A revalidation of an earlier year's stage belongs on its own promotion's document — the one
    /// the student is registered in — and the two level columns are what make it readable as a
    /// rattrapage rather than as a row filed in the wrong place.
    /// </summary>
    [Fact]
    public async Task A_retake_of_an_earlier_levels_stage_is_filed_under_the_promotion_the_student_is_in()
    {
        using var db = TestHarness.NewContext(nameof(A_retake_of_an_earlier_levels_stage_is_filed_under_the_promotion_the_student_is_in));
        var thirdYearStage = db.SeedCatalog();
        db.SeedLevel(levelId: 6, label: "6ème année", year: 6);
        var service = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(thirdYearStage, groupId: 1, groupLabel: "G1");

        // Registered in the 6ᵉ année, redoing a 3ᵉ année stage.
        var registration = db.SeedRegistration("Amina", "Benali", levelId: 6);
        db.SeedAssignment(registration, cohort);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();

        var sixth = await StagesHandler(db, writer)
            .Handle(new GetStageAssignmentsExportQuery(LevelId: 6), default);
        sixth.IsSuccess.Should().BeTrue();
        var sheet = writer.Captured!.Sheets[0];
        sheet.Rows.Should().HaveCount(1);
        Cell(sheet, 0, "Niveau").Should().Be("6ème année");
        Cell(sheet, 0, "Niveau du stage").Should().Be("3ème année");

        var third = await StagesHandler(db, writer)
            .Handle(new GetStageAssignmentsExportQuery(LevelId: TestHarness.LevelId), default);
        third.IsSuccess.Should().BeTrue();
        writer.Captured!.Sheets[0].Rows.Should().BeEmpty(
            "the row is on the 6ᵉ année's document, not on the promotion whose stage it happens to be");
    }

    // ── Ce que le document dit de ses propres blancs ─────────────────────────────────────────

    /// <summary>
    /// The report, reproduced: 2026-2027 held <b>90 rosters and 0 inscriptions rattachées</b>, so the
    /// roll came out with « Groupe » blank on all 5 932 lines — correctly — and was read as a broken
    /// export within minutes.
    /// </summary>
    /// <remarks>
    /// ⚠ The two causes call for opposite acts and a blank column collapses them: no roster at all
    /// means « découper la promotion », rosters holding nobody means « répartir les étudiants ». Same
    /// shape as <c>RepartitionSummary.DeclaredSlotCount</c>.
    /// </remarks>
    [Fact]
    public async Task Rosters_that_exist_but_hold_nobody_are_named_in_the_document()
    {
        using var db = TestHarness.NewContext(nameof(Rosters_that_exist_but_hold_nobody_are_named_in_the_document));
        db.SeedCatalog();
        db.SeedGroup(groupId: 10, groupNumber: 1, rotationGroup: "A");
        db.SeedGroup(groupId: 11, groupNumber: 2, rotationGroup: "B");
        // Registered, and in no roster — exactly the state 2026-2027 was in.
        db.SeedRegistration("Amina", "Benali");
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var sheet = writer.Captured!.Sheets.Single();

        Cell(sheet, 0, "Groupe").Should().BeNull("no registration carries a roster pointer");
        sheet.Notes.Should().NotBeNull();
        sheet.Notes!.Should().Contain(n => n.Contains("Aucune valeur dans cet export pour")
                                        && n.Contains("Groupe"));
        sheet.Notes!.Should().Contain(n => n.Contains("2 groupe(s) existent")
                                        && n.Contains("la répartition des étudiants ne l'est pas encore"));
    }

    /// <summary>
    /// The other cause, and it must not print the same sentence: nobody has cut the promotion at all.
    /// </summary>
    [Fact]
    public async Task With_no_roster_at_all_the_document_says_the_promotion_was_never_cut()
    {
        using var db = TestHarness.NewContext(nameof(With_no_roster_at_all_the_document_says_the_promotion_was_never_cut));
        db.SeedCatalog();
        db.SeedRegistration("Amina", "Benali");
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        writer.Captured!.Sheets.Single().Notes!
            .Should().Contain(n => n.Contains("aucun groupe n'existe encore"));
    }

    /// <summary>
    /// ⚠ The control. A note that fires whatever the data says is noise, and noise is dismissed —
    /// which would put the real one back out of sight.
    /// </summary>
    [Fact]
    public async Task A_column_that_carries_a_value_somewhere_is_not_reported_as_empty()
    {
        using var db = TestHarness.NewContext(nameof(A_column_that_carries_a_value_somewhere_is_not_reported_as_empty));
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(groupId: 10, groupNumber: 7, rotationGroup: "C");
        db.SeedRegistration("Amina", "Benali", group);
        // A second student with no roster: the column is partial, which is not the same as empty.
        db.SeedRegistration("Youssef", "Idrissi");
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var notes = writer.Captured!.Sheets.Single().Notes ?? [];

        notes.Should().NotContain(n => n.Contains("groupe(s) existent"),
            "the roster note is for a column empty on *every* row, not a partly-filled one");
        notes.Should().NotContain(n => n.Contains("Aucune valeur dans cet export pour") && n.Contains("Groupe"));
    }

    /// <summary>An empty file has no columns to call empty — the note must stay silent.</summary>
    [Fact]
    public async Task An_export_with_no_rows_reports_no_empty_columns()
    {
        using var db = TestHarness.NewContext(nameof(An_export_with_no_rows_reports_no_empty_columns));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StudentsHandler(db, writer).Handle(new GetStudentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var sheet = writer.Captured!.Sheets.Single();
        sheet.Rows.Should().BeEmpty();
        (sheet.Notes ?? []).Should().NotContain(n => n.Contains("Aucune valeur dans cet export"));
    }

    [Fact]
    public async Task The_stage_record_names_its_empty_columns_too()
    {
        using var db = TestHarness.NewContext(nameof(The_stage_record_names_its_empty_columns_too));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedAssignment(registration, cohort);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        // Nothing is graded, so « Note » carries nothing anywhere — and the document says so rather
        // than leaving a reader to wonder whether the marks were simply not read.
        writer.Captured!.Sheets[0].Notes!
            .Should().Contain(n => n.Contains("Aucune valeur dans cet export pour") && n.Contains("Note"));
    }

    // ── Créneaux et chef de service ──────────────────────────────

    /// <summary>
    /// The reported case, end to end. Under <c>SingleService</c> the publisher folds a run of three
    /// grid columns into <b>one</b> <c>ServicePeriod</c> — right, because the student stands in one
    /// service and is marked once — and the document said « Période unique » with the three columns
    /// nowhere in it.
    ///
    /// <para>⚠ Both facts have to be on the row: <b>one</b> période and <b>three</b> créneaux. The
    /// old columns are asserted unchanged here on purpose — the fold was never the defect, the
    /// silence about what it folded was.</para>
    /// </summary>
    [Fact]
    public async Task A_single_service_run_reports_one_periode_and_the_three_creneaux_behind_it()
    {
        using var db = TestHarness.NewContext(nameof(A_single_service_run_reports_one_periode_and_the_three_creneaux_behind_it));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Gynécologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        SeedSingleServiceRun(db, assignment, stage, cohort, service, columns: 3);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();

        var stages = writer.Captured!.Sheets[0];
        Cell(stages, 0, "Découpage").Should().Be("Période unique", "the fold itself was never wrong");
        Number(stages, 0, "Nb périodes").Should().Be(1);
        Number(stages, 0, "Nb créneaux").Should().Be(3, "the grid authored three columns for this run");
        Cell(stages, 0, "Créneaux").Should().Be("P1-P3");
        Cell(stages, 0, "Détail des périodes").Should().Contain("créneaux P1-P3");

        var periods = writer.Captured!.Sheets[1];
        periods.Rows.Should().ContainSingle(
            "a run marked once stays one row — repeated per column the note would be counted three times");
        Number(periods, 0, "Nb créneaux").Should().Be(3);
        Cell(periods, 0, "Créneaux").Should().Be("P1-P3");
        Cell(periods, 0, "Détail des créneaux").Should()
            .Contain("P1 · 01/01/2026 – 28/01/2026").And
            .Contain("P3 · 01/03/2026 – 28/03/2026");
    }

    /// <summary>
    /// ⚠ The control. A période that came from no grid — imported history, a délocalisation, a
    /// revalidation — has no créneau to name, and the cell is left <b>empty</b> rather than printing
    /// a 0 that reads as a count which failed. « Origine » already says « Hors grille » for it.
    /// </summary>
    [Fact]
    public async Task An_ad_hoc_periode_names_no_creneau_rather_than_reporting_zero()
    {
        using var db = TestHarness.NewContext(nameof(An_ad_hoc_periode_names_no_creneau_rather_than_reporting_zero));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Cardiologie");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var periods = writer.Captured!.Sheets[1];

        Cell(periods, 0, "Origine").Should().Be("Hors grille");
        periods.Rows[0][ColumnOf(periods, "Nb créneaux")].HasValue.Should().BeFalse();
        Cell(periods, 0, "Créneaux").Should().BeNull();
    }

    /// <summary>
    /// ⚠ 140 of the 148 imported services name their professor only in a free-text note the Access
    /// base last recorded, and that note is <b>undated</b>. The name is printed — on 95 % of the
    /// document it is the only one there is — and the column beside it says where it came from, so
    /// nothing claims this student served under a chef the base cannot date.
    /// </summary>
    [Fact]
    public async Task The_legacy_chef_note_is_printed_and_reported_as_a_note()
    {
        using var db = TestHarness.NewContext(nameof(The_legacy_chef_note_is_printed_and_reported_as_a_note));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Cardiologie");
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();

        var stages = writer.Captured!.Sheets[0];
        Cell(stages, 0, "Chef(s) de service").Should().Be("Pr.A.Settaf");
        Cell(stages, 0, "Origine du chef").Should().Be("Note (import)");

        var periods = writer.Captured!.Sheets[1];
        Cell(periods, 0, "Chef de service").Should().Be("Pr.A.Settaf");
        Cell(periods, 0, "Origine du chef").Should().Be("Note (import)");
    }

    /// <summary>
    /// ⚠ <b>The temporary policy, and it is the whole subject of the change.</b> A chef linked in
    /// Personnel is <em>ignored</em> while <see cref="ServiceChefPolicy.InForce"/> is
    /// <see cref="ServiceChefSourcePolicy.SourceNoteOnly"/>: the two <c>ServiceChefAssignment</c>
    /// rows in the base were linked to try the mechanism out, so resolving them prints a test
    /// account's name beside real students.
    ///
    /// <para>The authority order itself stays covered in <c>ServiceChefDirectoryTests</c>, which is
    /// what makes flipping the constant back a safe one-line change rather than a rewrite.</para>
    /// </summary>
    [Fact]
    public async Task A_chef_linked_in_personnel_is_ignored_in_favour_of_the_note()
    {
        using var db = TestHarness.NewContext(nameof(A_chef_linked_in_personnel_is_ignored_in_favour_of_the_note));
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(Guid.NewGuid());
        chef.FirstName = "Nadia";
        chef.LastName = "Bennis";
        var service = db.SeedService(1, "Service de Cardiologie", chef);
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var periods = writer.Captured!.Sheets[1];

        Cell(periods, 0, "Chef de service").Should().Be("Pr.A.Settaf",
            "the linked chefs are test rows; the import note is the faculty's own last record");
        Cell(periods, 0, "Origine du chef").Should().Be("Note (import)",
            "narrowing the sources is not a licence to stop saying the name is undated");
    }

    /// <summary>
    /// The cost of the policy, stated so nobody reads the blank as a lost join: a service whose only
    /// chef is a linked one names <b>nobody</b>. Deliberate — a blank cell says less wrongly than
    /// the wrong name, and « Origine du chef » stays empty with it rather than claiming a source.
    /// </summary>
    [Fact]
    public async Task A_service_whose_only_chef_is_a_link_prints_no_name_at_all()
    {
        using var db = TestHarness.NewContext(nameof(A_service_whose_only_chef_is_a_link_prints_no_name_at_all));
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(Guid.NewGuid());
        chef.FirstName = "Nadia";
        chef.LastName = "Bennis";
        var service = db.SeedService(1, "Service de Cardiologie", chef);
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        var periods = writer.Captured!.Sheets[1];

        Cell(periods, 0, "Chef de service").Should().BeNull();
        Cell(periods, 0, "Origine du chef").Should().BeNull(
            "ChefOrigin names a source only for a name it actually printed");
    }

    /// <summary>
    /// ⚠ A column reading the same thing on every row is the mirror of one reading nothing: it looks
    /// like a value the export hard-coded rather than a policy somebody chose, and the blank it
    /// leaves on an affectation-only service has no explanation on the page. The note carries both,
    /// and only onto the sheets that print a chef — Synthèse has no such column.
    /// </summary>
    [Fact]
    public async Task The_sheets_that_print_a_chef_say_the_name_comes_from_the_import_note()
    {
        using var db = TestHarness.NewContext(nameof(The_sheets_that_print_a_chef_say_the_name_comes_from_the_import_note));
        var stage = db.SeedCatalog();
        var service = db.SeedService(1, "Service de Cardiologie");
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");
        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14.5m);
        await db.SaveChangesAsync();

        var writer = new CapturingWriter();
        var result = await StagesHandler(db, writer).Handle(new GetStageAssignmentsExportQuery(), default);

        result.IsSuccess.Should().BeTrue();
        string expected = ExportNotes.ChefSourceNote(ServiceChefPolicy.InForce)!;

        writer.Captured!.Sheets[0].Notes.Should().Contain(expected);
        writer.Captured!.Sheets[1].Notes.Should().Contain(expected);
        writer.Captured!.Sheets[2].Notes.Should().NotContain(expected,
            "Synthèse names no chef, so the note would answer a question its reader never asked");
    }

    /// <summary>The note is silent under the full authority order — one that fires whatever the
    /// policy says is noise, and noise is dismissed, which puts the real ones out of sight.</summary>
    [Fact]
    public void The_chef_source_note_is_silent_when_the_full_authority_order_is_in_force()
    {
        ExportNotes.ChefSourceNote(ServiceChefSourcePolicy.Authority).Should().BeNull();
        ExportNotes.ChefSourceNote(ServiceChefSourcePolicy.SourceNoteOnly).Should().NotBeNull();
    }
}
