using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Delocalization.Bulk;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

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
        DelocalizationTargets targets, int confirmed, string reason = "Saturation — accueil à Kénitra") =>
        new(TestHarness.StageId, ExternalServiceId, reason, targets, confirmed,
            StartDate: Start, EndDate: End);

    private static PreviewBulkDelocalizationQuery Preview(DelocalizationTargets targets) =>
        new(TestHarness.StageId, ExternalServiceId, targets, StartDate: Start, EndDate: End);

    [Fact]
    public async Task A_whole_roster_is_delocalized_by_its_id()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-roster");
        var s = await SeedAsync(db);

        var targets = new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

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

        var targets = new DelocalizationTargets(
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
            .Handle(Preview(new DelocalizationTargets(Identifiers: ["  r130896  "])), default);

        preview.Value.ApplicableCount.Should().Be(1);
    }

    [Fact]
    public async Task An_identifier_that_matches_nobody_is_reported_rather_than_dropped()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-unmatched");
        await SeedAsync(db, studentCount: 1);

        var preview = await db.BulkDelocalizePreview()
            .Handle(Preview(new DelocalizationTargets(Identifiers: ["INCONNU-1"])), default);

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
            .Handle(Preview(new DelocalizationTargets(Identifiers: [appogee])), default);

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

        var targets = new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

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
        var targets = new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

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
            .Handle(Preview(new DelocalizationTargets(RegistrationIds: [loose.Id])), default);

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
            .Handle(Preview(new DelocalizationTargets(RegistrationIds: [registration.Id])), default);

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

        var targets = new DelocalizationTargets(RegistrationIds: [unplanned.Id]);

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
        var targets = new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

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
            .Handle(Preview(new DelocalizationTargets(AcademicGroupIds: [cohort.AcademicGroupId])), default);

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
            Preview(new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId])), default);

        var report = preview.Value;

        report.TotalRowCount.Should().Be(total + 1);
        report.ApplicableCount.Should().Be(total, "the count is measured before the cap");
        report.RefusedCount.Should().Be(1);
        report.Rows.Should().HaveCount(BulkDelocalizationReport.RowCap);
        report.RowsTruncated.Should().BeTrue();

        report.Rows.Should().Contain(r => r.Status == BulkDelocalizationRowStatus.AlreadyMarked,
            "a refusal is a row somebody has to act on, so it is never the one dropped");
    }

    [Fact]
    public async Task The_act_is_reserved_to_scolarite()
    {
        await using var db = TestHarness.NewContext("bulk-deloc-scope");
        var s = await SeedAsync(db, studentCount: 1);
        var targets = new DelocalizationTargets(AcademicGroupIds: [s.Cohort.AcademicGroupId]);

        var result = await db.BulkDelocalizeHandler(db.StrangerAuthorizer())
            .Handle(Apply(targets, confirmed: 1), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BulkDelocalizationErrors.NotAllowed);
        (await db.ServicePeriods.CountAsync(p => p.IsDelocalized)).Should().Be(0);
    }
}
