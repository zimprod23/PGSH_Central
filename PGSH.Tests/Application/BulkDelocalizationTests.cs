using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Delocalization;
using PGSH.Application.Stages.Delocalization.Bulk;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using PGSH.Application.Students.Selection;

namespace PGSH.Tests.Application;

// Délocalising a selection of students on one stage — whole rosters, named students, or a list
// pasted from the form the faculty circulated. The preview and the apply run the same planner, so
// every case here is asserted on the preview and, where it writes, on the store afterwards.
public class BulkDelocalizationTests
{
    private const int ExternalServiceId = 60;
    private const int HomeServiceId     = 1;

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record Scenario(Stage Stage, Cohort Cohort, List<Registration> Students);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, int studentCount = 3)
    {
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Stage hors CHU — Kénitra", isExternal: true);
        var home = db.SeedService(HomeServiceId, "Cardiologie");

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var students = new List<Registration>();

        for (int i = 0; i < studentCount; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i}", $"Nom{i}", cohort.AcademicGroup);
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, home, Start, End, started: false);
            students.Add(registration);
        }

        await db.SaveChangesAsync();
        return new Scenario(stage, cohort, students);
    }

    private static ApplyBulkDelocalizationCommand Apply(
        StudentTargets targets, int confirmed, string reason = "Saturation — accueil à Kénitra") =>
        new(TestHarness.StageId, ExternalServiceId, reason, targets, confirmed,
            StartDate: Start, EndDate: End);

    private static PreviewBulkDelocalizationQuery Preview(StudentTargets targets) =>
        new(TestHarness.StageId, ExternalServiceId, targets, StartDate: Start, EndDate: End);

    [Fact]
    public async Task A_whole_roster_is_delocalized_by_its_id()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-roster");
        var s = await SeedAsync(db);

        var targets = new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        var preview = await db.BulkDelocalizePreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(3);

        var applied = await db.BulkDelocalizeHandler().Handle(Apply(targets, confirmed: 3), default);

        applied.IsSuccess.Should().BeTrue();
        var periods = await db.ServicePeriods.Where(p => p.IsDelocalized).ToListAsync();
        periods.Should().HaveCount(3);
        periods.Should().OnlyContain(p => p.ServiceId == ExternalServiceId);
    }

    [Fact]
    public async Task Named_students_and_a_pasted_list_are_unioned_with_the_rosters()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-union");
        var s = await SeedAsync(db);

        // The same student named twice — once by id, once by his Apogée — is still one row.
        var appogee = await db.Students
            .Where(x => x.Id == s.Students[0].StudentId)
            .Select(x => x.Appogee)
            .FirstAsync();

        var targets = new StudentTargets(
            RegistrationIds: [s.Students[0].Id, s.Students[1].Id],
            Identifiers:     [appogee]);

        var preview = await db.BulkDelocalizePreview().Handle(Preview(targets), default);

        preview.Value.ApplicableCount.Should().Be(2);
        preview.Value.Rows.Should().HaveCount(2);
    }

    // ⚠ Both columns, case-insensitively. A single field left un-lowered is a silent miss.
    [Fact]
    public async Task A_pasted_identifier_matches_the_cne_whatever_its_case()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-cne-case");
        var s = await SeedAsync(db, studentCount: 1);

        var student = await db.Students.FirstAsync(x => x.Id == s.Students[0].StudentId);
        student.CNE = "R130896";
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(Identifiers: ["  r130896  "])), default);

        preview.Value.ApplicableCount.Should().Be(1);
    }

    [Fact]
    public async Task An_identifier_that_matches_nobody_is_reported_rather_than_dropped()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-unmatched");
        await SeedAsync(db, studentCount: 1);

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(Identifiers: ["INCONNU-1"])), default);

        var row = preview.Value.Rows.Should().ContainSingle().Subject;
        row.Status.Should().Be(BulkDelocalizationRowStatus.NotFound);
        row.SourceIdentifier.Should().Be("INCONNU-1", "an unmatched line has to be findable in the file it came from");
        preview.Value.ApplicableCount.Should().Be(0);
    }

    // « je ne le trouve pas » and « il est en 5ᵉ, pas en 6ᵉ » are two different corrections.
    [Fact]
    public async Task A_student_registered_in_another_year_is_named_as_such()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-wrong-year");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);
        db.SeedCohort(stage, 10, "Groupe 10");

        var lastYear = db.SeedAcademicYear(
            2, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 7, 31));
        var group = db.SeedGroup(88, groupNumber: 88, academicYearId: lastYear.Id);
        var registration = db.SeedRegistration("Anas", "Idrissi", group, academicYearId: lastYear.Id);
        await db.SaveChangesAsync();

        var appogee = await db.Students.Where(x => x.Id == registration.StudentId)
            .Select(x => x.Appogee).FirstAsync();

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(Identifiers: [appogee])), default);

        preview.Value.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(BulkDelocalizationRowStatus.WrongYear);
    }

    // ⚠ Skipped, never silently: the row is on the report and the others are still written.
    [Fact]
    public async Task A_student_whose_stage_is_already_marked_is_refused_and_the_rest_still_apply()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-marked");
        var s = await SeedAsync(db, studentCount: 2);

        var home = await db.Services.FirstAsync(x => x.Id == HomeServiceId);
        var marked = db.SeedRegistration("Salma", "Kabbaj", s.Cohort.AcademicGroup);
        db.SeedGradedAssignment(marked, s.Cohort, home, mark: 12m);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        var preview = await db.BulkDelocalizePreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(2);
        preview.Value.RefusedCount.Should().Be(1);
        preview.Value.Rows.Should().ContainSingle(r => r.Status == BulkDelocalizationRowStatus.AlreadyMarked);

        var applied = await db.BulkDelocalizeHandler().Handle(Apply(targets, confirmed: 2), default);

        applied.IsSuccess.Should().BeTrue();
        var stillMarked = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.RegistrationId == marked.Id);

        stillMarked.ServicePeriods.Should().ContainSingle()
            .Which.ServiceId.Should().Be(HomeServiceId, "a mark is never erased by a list");
    }

    // ⚠ The whole reason ConfirmedCount exists: the act lands on students nobody typed the name of.
    [Fact]
    public async Task A_student_who_joined_the_roster_after_the_preview_refuses_the_apply()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-drift");
        var s = await SeedAsync(db, studentCount: 2);
        var targets = new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        var preview = await db.BulkDelocalizePreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(2);

        db.SeedRegistration("Nouveau", "Venu", s.Cohort.AcademicGroup);
        await db.SaveChangesAsync();

        var applied = await db.BulkDelocalizeHandler()
            .Handle(Apply(targets, confirmed: preview.Value.ApplicableCount), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("BulkDelocalization.CountMismatch");
        (await db.ServicePeriods.CountAsync(p => p.IsDelocalized))
            .Should().Be(0, "a refused act writes nothing at all");
    }

    [Fact]
    public async Task A_student_with_no_roster_is_refused_by_name()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-no-roster");
        await SeedAsync(db, studentCount: 1);
        var loose = db.SeedRegistration("Sans", "Groupe", group: null);
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [loose.Id])), default);

        preview.Value.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(BulkDelocalizationRowStatus.NoRoster);
    }

    [Fact]
    public async Task A_student_whose_roster_has_no_cohorte_on_this_stage_is_refused_by_name()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-no-cohort");
        await SeedAsync(db, studentCount: 1);

        var orphan = db.SeedGroup(77, groupNumber: 77);
        var registration = db.SeedRegistration("Hamza", "Berrada", orphan);
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [registration.Id])), default);

        preview.Value.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(BulkDelocalizationRowStatus.NoCohort);
    }

    [Fact]
    public async Task A_student_the_stage_was_never_planned_for_is_delocalized_all_the_same()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-unplanned");
        var s = await SeedAsync(db, studentCount: 1);
        var unplanned = db.SeedRegistration("Jamais", "Réparti", s.Cohort.AcademicGroup);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(RegistrationIds: [unplanned.Id]);

        var preview = await db.BulkDelocalizePreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(1);

        var applied = await db.BulkDelocalizeHandler().Handle(Apply(targets, confirmed: 1), default);

        applied.IsSuccess.Should().BeTrue();
        var created = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.RegistrationId == unplanned.Id);

        created.CurrentCohortId.Should().Be(s.Cohort.Id);
        created.ServicePeriods.Should().ContainSingle().Which.IsDelocalized.Should().BeTrue();
        created.Status.Should().Be(InternshipStatus.Completed);
    }

    [Fact]
    public async Task Re_sending_the_same_list_replaces_rather_than_stacking()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-idempotent");
        var s = await SeedAsync(db, studentCount: 2);
        var targets = new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        await db.BulkDelocalizeHandler().Handle(Apply(targets, confirmed: 2), default);

        var second = await db.BulkDelocalizePreview().Handle(Preview(targets), default);
        second.Value.ReplacedCount.Should().Be(2);
        second.Value.Rows.Should().OnlyContain(r => r.Status == BulkDelocalizationRowStatus.WillReplace);

        var applied = await db.BulkDelocalizeHandler()
            .Handle(Apply(targets, confirmed: 2, reason: "Dates corrigées"), default);

        applied.IsSuccess.Should().BeTrue();
        (await db.ServicePeriods.CountAsync(p => p.IsDelocalized)).Should().Be(2);
    }

    [Fact]
    public async Task A_rotation_already_under_way_is_applied_and_flagged_as_a_loss()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-underway");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Kénitra", isExternal: true);
        var home = db.SeedService(HomeServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Karim", "Sabri", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, home, Start, End, started: true);
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new StudentTargets(AcademicGroupIds: [cohort.AcademicGroupId])), default);

        preview.Value.UnderwayCount.Should().Be(1);
        preview.Value.Rows.Should().ContainSingle()
            .Which.Status.Should().Be(BulkDelocalizationRowStatus.WillDropUnderway);
    }

    // ⚠ A selection is a whole promotion when the operator asks for one — 933 students on the 3ᵉ MED
    // — and the report is a single object. Capping it is the same rule that stopped one group's 4 725
    // students travelling in one response; what makes the cap safe is that the counts are measured
    // before it and the refusals are the rows kept.
    [Fact]
    public async Task A_selection_larger_than_the_cap_is_trimmed_and_the_counts_are_not()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-cap");
        int total = BulkDelocalizationReport.RowCap + 5;
        var s = await SeedAsync(db, studentCount: total);

        // One refusal, deliberately seeded last so a naive Take() would drop it.
        var home = await db.Services.FirstAsync(x => x.Id == HomeServiceId);
        var marked = db.SeedRegistration("Zzz", "Dernier", s.Cohort.AcademicGroup);
        db.SeedGradedAssignment(marked, s.Cohort, home, mark: 12m);
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview().Handle(
            Preview(new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId])), default);

        var report = preview.Value;

        report.TotalRowCount.Should().Be(total + 1);
        report.ApplicableCount.Should().Be(total, "the count is measured before the cap");
        report.RefusedCount.Should().Be(1);
        report.Rows.Should().HaveCount(BulkDelocalizationReport.RowCap);
        report.RowsTruncated.Should().BeTrue();

        report.Rows.Should().Contain(r => r.Status == BulkDelocalizationRowStatus.AlreadyMarked,
            "a refusal is a row somebody has to act on, so it is never the one dropped");
    }

    // ---------------------------------------------------------------------------------------------
    // The window. Until 2026-09-10 it was resolved ONCE, off the stage, before the students were even
    // known - so every student of the act was dated by the whole axis whatever partition he was in.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Two partitions, two passages, one stage — and one act naming both.</summary>
    private sealed record TwoPartitions(Cohort First, Cohort Second, Registration A, Registration B);

    private static async Task<TwoPartitions> SeedTwoPartitionsAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Stage hors CHU — Kénitra", isExternal: true);
        var home = db.SeedService(HomeServiceId, "Cardiologie");

        var groupA = db.SeedGroup(11, 11, rotationGroup: "A");
        var groupB = db.SeedGroup(12, 12, rotationGroup: "B");
        var cohortA = db.SeedCohortFor(stage, groupA, 11);
        var cohortB = db.SeedCohortFor(stage, groupB, 12);

        var p1 = db.SeedSlot(stage, 1, 1, new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 13));
        var p2 = db.SeedSlot(stage, 2, 2, new DateOnly(2027, 2, 22), new DateOnly(2027, 3, 25));

        // A crosses in P1, B in P2 — which is exactly what an axis min/max flattens away.
        db.SeedSlotAssignment(1, cohortA, p1, home);
        db.SeedSlotAssignment(2, cohortB, p2, home);

        var a = db.SeedRegistration("Amine", "Berrada", groupA);
        var b = db.SeedRegistration("Btissam", "Cherkaoui", groupB);
        db.SeedAssignment(a, cohortA);
        db.SeedAssignment(b, cohortB);

        await db.SaveChangesAsync();
        return new TwoPartitions(cohortA, cohortB, a, b);
    }

    // ⚠ The defect, stated as a test: one act, two groups, and each dated by its own passage. Asked
    // the old way both rows read 16/11/2026 -> 25/03/2027 — four months for a stage each group serves
    // in four weeks, overlapping every other stage of their year.
    [Fact]
    public async Task Each_cohorte_is_dated_by_its_own_passage_not_by_the_whole_axis()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-partitions");
        var s = await SeedTwoPartitionsAsync(db);

        var targets = new StudentTargets(AcademicGroupIds: [11, 12]);
        var preview = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(TestHarness.StageId, ExternalServiceId, targets),
            default);

        var report = preview.Value;
        report.ApplicableCount.Should().Be(2);

        var rowA = report.Rows.Single(r => r.RegistrationId == s.A.Id);
        var rowB = report.Rows.Single(r => r.RegistrationId == s.B.Id);

        rowA.StartDate.Should().Be(new DateOnly(2026, 11, 16));
        rowA.EndDate.Should().Be(new DateOnly(2026, 12, 13));
        rowB.StartDate.Should().Be(new DateOnly(2027, 2, 22));
        rowB.EndDate.Should().Be(new DateOnly(2027, 3, 25));

        rowA.WindowSource.Should().Be(DelocalizationWindowSource.Cohort);
        rowB.WindowSource.Should().Be(DelocalizationWindowSource.Cohort);
    }

    // ⚠ Says what the blank means. Two windows and no single pair of dates true of the act, so the
    // header carries none — and DistinctWindowCount is what stops the null being guessed at.
    [Fact]
    public async Task A_selection_spanning_two_windows_states_that_it_has_no_single_one()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-header");
        await SeedTwoPartitionsAsync(db);

        var preview = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(
                TestHarness.StageId, ExternalServiceId, new StudentTargets(AcademicGroupIds: [11, 12])),
            default);

        var report = preview.Value;

        report.DistinctWindowCount.Should().Be(2);
        report.WindowsDiffer.Should().BeTrue();
        report.StartDate.Should().BeNull();
        report.EndDate.Should().BeNull();

        // One group alone: the act does have a single window, and it is that group's passage.
        var single = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(
                TestHarness.StageId, ExternalServiceId, new StudentTargets(AcademicGroupIds: [11])),
            default);

        single.Value.DistinctWindowCount.Should().Be(1);
        single.Value.WindowsDiffer.Should().BeFalse();
        single.Value.StartDate.Should().Be(new DateOnly(2026, 11, 16));
        single.Value.EndDate.Should().Be(new DateOnly(2026, 12, 13));
    }

    // And what is previewed is what is written — the whole claim the planner rests on.
    [Fact]
    public async Task The_apply_writes_each_student_under_his_own_window()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-apply");
        var s = await SeedTwoPartitionsAsync(db);

        var applied = await db.BulkDelocalizeHandler().Handle(
            new ApplyBulkDelocalizationCommand(
                TestHarness.StageId, ExternalServiceId, "Kénitra",
                new StudentTargets(AcademicGroupIds: [11, 12]), ConfirmedCount: 2),
            default);

        applied.IsSuccess.Should().BeTrue();

        var periods = await db.ServicePeriods
            .Include(p => p.InternshipAssignment)
            .Where(p => p.IsDelocalized)
            .ToListAsync();

        periods.Should().HaveCount(2);

        var wroteA = periods.Single(p => p.InternshipAssignment.RegistrationId == s.A.Id);
        var wroteB = periods.Single(p => p.InternshipAssignment.RegistrationId == s.B.Id);

        wroteA.StartDate.Should().Be(new DateOnly(2026, 11, 16));
        wroteA.EndDate.Should().Be(new DateOnly(2026, 12, 13));
        wroteB.StartDate.Should().Be(new DateOnly(2027, 2, 22));
        wroteB.EndDate.Should().Be(new DateOnly(2027, 3, 25));
    }

    // ⚠ A group with no cell is dated by the whole stage, because nothing else is known about it —
    // and the report says so instead of passing four months off as a measurement.
    [Fact]
    public async Task A_cohorte_with_no_cell_is_dated_by_the_axis_and_the_report_names_it()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-axis");
        var stage = db.SeedCatalog();
        db.SeedService(ExternalServiceId, "Externe", isExternal: true);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        db.SeedRegistration("Nadia", "Ouazzani", cohort.AcademicGroup);
        db.SeedSlot(stage, 1, 1, new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 13));
        db.SeedSlot(stage, 2, 2, new DateOnly(2027, 2, 22), new DateOnly(2027, 3, 25));
        await db.SaveChangesAsync();

        var preview = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(
                TestHarness.StageId, ExternalServiceId,
                new StudentTargets(AcademicGroupIds: [cohort.AcademicGroupId])),
            default);

        var row = preview.Value.Rows.Single();

        row.WindowSource.Should().Be(DelocalizationWindowSource.StageAxis);
        row.WindowIsStageWide.Should().BeTrue();
        row.StartDate.Should().Be(new DateOnly(2026, 11, 16));
        row.EndDate.Should().Be(new DateOnly(2027, 3, 25));
        preview.Value.StageWideWindowCount.Should().Be(1);
    }

    // Dates the operator names are a statement about what the external hospital did. Deriving over
    // them would be the app overruling him — so they govern every row, and say where they came from.
    [Fact]
    public async Task Dates_the_operator_names_govern_every_row()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-named");
        await SeedTwoPartitionsAsync(db);

        var preview = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(
                TestHarness.StageId, ExternalServiceId, new StudentTargets(AcademicGroupIds: [11, 12]),
                StartDate: Start, EndDate: End),
            default);

        var report = preview.Value;

        report.DistinctWindowCount.Should().Be(1);
        report.StartDate.Should().Be(Start);
        report.EndDate.Should().Be(End);
        report.StageWideWindowCount.Should().Be(0);
        report.Rows.Should().OnlyContain(r => r.WindowSource == DelocalizationWindowSource.Named);
    }

    // ⚠ Still refuses rather than inventing a window: no créneau anywhere on the stage, and no cell
    // to read one off either.
    [Fact]
    public async Task A_stage_with_no_creneaux_at_all_is_still_refused_by_name()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-window-none");
        var s = await SeedAsync(db, studentCount: 2);

        var preview = await db.BulkDelocalizePreview().Handle(
            new PreviewBulkDelocalizationQuery(
                TestHarness.StageId, ExternalServiceId,
                new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId])),
            default);

        preview.IsFailure.Should().BeTrue();
        preview.Error.Code.Should().Be("Delocalizations.NoWindow");
    }

    [Fact]
    public async Task The_act_is_reserved_to_scolarite()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-scope");
        var s = await SeedAsync(db, studentCount: 1);
        var targets = new StudentTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        var result = await db.BulkDelocalizeHandler(db.StrangerAuthorizer())
            .Handle(Apply(targets, confirmed: 1), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BulkDelocalizationErrors.NotAllowed);
        (await db.ServicePeriods.CountAsync(p => p.IsDelocalized)).Should().Be(0);
    }
}
