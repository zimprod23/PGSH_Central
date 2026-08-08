using FluentAssertions;
using PGSH.Application.AcademicGroups.GetById;
using PGSH.Application.AcademicGroups.GetMany;
using PGSH.Application.Stages.Cohorts.GetByStage;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Real data broke these: the legacy import parks every registration with no group number into a
// per-year "Non réparti" bucket, which holds 4,725 students for 2025-2026, and a stage accumulates a
// cohort per (group, year) — 681 for "Chirurgie". Both used to be returned whole.
public class GroupAndCohortPagingTests
{
    private static AcademicGroup SeedGroupWithStudents(ApplicationDbContext db, int groupId, int students)
    {
        var stage = db.Stages.Local.FirstOrDefault() ?? db.SeedCatalog();
        var cohort = db.SeedCohort(stage, groupId, "Non réparti");

        for (int i = 0; i < students; i++)
            db.SeedRegistration($"Etudiant{i:D3}", $"Nom{i:D3}", cohort.AcademicGroup);

        return cohort.AcademicGroup;
    }

    [Fact]
    public async Task A_large_roster_comes_back_one_page_at_a_time()
    {
        await using var db = TestHarness.NewContext("group-page");
        var group = SeedGroupWithStudents(db, 10, students: 120);
        await db.SaveChangesAsync();

        var result = await new GetGroupByIdQueryHandler(db).Handle(
            new GetGroupByIdQuery(group.Id, PageNumber: 1, PageSize: 25), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Students.Items.Should().HaveCount(25);
        result.Value.Students.TotalCount.Should().Be(120);
        result.Value.Students.HasNextPage.Should().BeTrue();

        // The header count is the whole roster, not the page — the UI shows it without loading anyone.
        result.Value.StudentCount.Should().Be(120);
    }

    [Fact]
    public async Task A_later_page_returns_different_students()
    {
        await using var db = TestHarness.NewContext("group-page2");
        var group = SeedGroupWithStudents(db, 10, students: 60);
        await db.SaveChangesAsync();

        var handler = new GetGroupByIdQueryHandler(db);
        var first = await handler.Handle(new GetGroupByIdQuery(group.Id, 1, 25), default);
        var second = await handler.Handle(new GetGroupByIdQuery(group.Id, 2, 25), default);

        second.Value.Students.Items.Should().HaveCount(25);
        second.Value.Students.Items.Select(s => s.RegistrationId)
            .Should().NotIntersectWith(first.Value.Students.Items.Select(s => s.RegistrationId));
    }

    [Fact]
    public async Task The_roster_can_be_searched_because_scrolling_thousands_is_not_an_option()
    {
        await using var db = TestHarness.NewContext("group-search");
        var stage = db.SeedCatalog();
        var cohort = db.SeedCohort(stage, 10, "Non réparti");
        db.SeedRegistration("Omar", "Tazi", cohort.AcademicGroup);
        db.SeedRegistration("Salma", "Kabbaj", cohort.AcademicGroup);
        await db.SaveChangesAsync();

        var result = await new GetGroupByIdQueryHandler(db).Handle(
            new GetGroupByIdQuery(cohort.AcademicGroupId, SearchTerm: "kabbaj"), default);

        result.Value.Students.Items.Should().ContainSingle()
            .Which.FullName.Should().Contain("Kabbaj");
        result.Value.Students.TotalCount.Should().Be(1);
        // The header still reports the real roster size, not the filtered count.
        result.Value.StudentCount.Should().Be(2);
    }

    [Fact]
    public async Task Search_is_case_insensitive_on_every_field_it_claims_to_cover()
    {
        await using var db = TestHarness.NewContext("group-search-case");
        var stage = db.SeedCatalog();
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Omar", "Tazi", cohort.AcademicGroup);
        registration.Student.CNE = "R1234567";
        await db.SaveChangesAsync();

        var handler = new GetGroupByIdQueryHandler(db);

        foreach (string term in new[] { "OMAR", "tazi", "r1234567", "R1234567" })
        {
            var result = await handler.Handle(
                new GetGroupByIdQuery(cohort.AcademicGroupId, SearchTerm: term), default);
            result.Value.Students.Items.Should().ContainSingle($"'{term}' should match");
        }
    }

    [Fact]
    public async Task An_unknown_group_is_still_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("group-missing");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await new GetGroupByIdQueryHandler(db).Handle(new GetGroupByIdQuery(999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.NotFound");
    }

    [Fact]
    public async Task The_group_list_is_paged_and_reports_roster_sizes()
    {
        await using var db = TestHarness.NewContext("groups-list");
        var stage = db.SeedCatalog();
        for (int g = 1; g <= 8; g++)
        {
            var cohort = db.SeedCohort(stage, g, $"Groupe {g}");
            db.SeedRegistration($"A{g}", $"B{g}", cohort.AcademicGroup);
        }
        await db.SaveChangesAsync();

        var result = await new GetAcademicGroupsQueryHandler(db).Handle(
            new GetAcademicGroupsQuery(PageNumber: 1, PageSize: 5), default);

        result.Value.Items.Should().HaveCount(5);
        result.Value.TotalCount.Should().Be(8);
        result.Value.Items.Should().OnlyContain(g => g.StudentCount == 1);
    }

    [Fact]
    public async Task Cohorts_of_a_stage_are_scoped_to_one_academic_year()
    {
        // Without this filter every year the stage ever ran comes back at once.
        await using var db = TestHarness.NewContext("cohorts-year");
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedCohort(stage, 10, "Groupe 10");                                       // current year
        db.SeedCohort(stage, 20, "Groupe 20", TestHarness.PreviousYearId);           // previous year
        await db.SaveChangesAsync();

        var handler = new GetCohortByStageIdQueryHandler(db);

        var unscoped = await handler.Handle(new GetCohortsByStageQuery(stage.Id), default);
        unscoped.Value.TotalCount.Should().Be(2);

        var scoped = await handler.Handle(
            new GetCohortsByStageQuery(stage.Id, AcademicYearId: TestHarness.CurrentYearId), default);
        scoped.Value.TotalCount.Should().Be(1);
        scoped.Value.Items.Should().ContainSingle().Which.AcademicGroupId.Should().Be(10);
    }

    [Fact]
    public async Task Cohorts_of_a_stage_are_paged()
    {
        await using var db = TestHarness.NewContext("cohorts-page");
        var stage = db.SeedCatalog();
        for (int g = 1; g <= 12; g++) db.SeedCohort(stage, g, $"Groupe {g}");
        await db.SaveChangesAsync();

        var result = await new GetCohortByStageIdQueryHandler(db).Handle(
            new GetCohortsByStageQuery(stage.Id, PageNumber: 1, PageSize: 5), default);

        result.Value.Items.Should().HaveCount(5);
        result.Value.TotalCount.Should().Be(12);
        result.Value.HasNextPage.Should().BeTrue();
    }
}
