using FluentAssertions;
using PGSH.Application.AcademicGroups.Partitioning;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The Plan macro tab's partition summary, computed where the rows are.
///
/// <para>⚠ It used to be derived in the browser from <c>GET /groups</c> at <c>pageSize: 200</c>. A
/// promotion adds ~100 rosters a year, so past 200 the tab would have shown a partition smaller than it
/// is and under-reported « N groupes sans partition » — the number whose whole job is to tell an admin
/// that a gap-fill is owed. A count read off a page is not a count.</para>
/// </summary>
public class PromotionPartitioningTests
{
    private const int SixthYearId = 6;

    private static GetPromotionPartitioningQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    [Fact]
    public async Task It_counts_the_whole_promotion_and_nothing_else()
    {
        await using var db = TestHarness.NewContext(nameof(It_counts_the_whole_promotion_and_nothing_else));
        db.SeedCatalog();

        db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedGroup(2, 2, rotationGroup: "A");
        db.SeedGroup(3, 3, rotationGroup: "A");
        db.SeedGroup(4, 4, rotationGroup: "B");
        db.SeedGroup(5, 5, rotationGroup: "B");
        db.SeedGroup(6, 6);

        // Another promotion, cut its own way, and the year's « Non réparti » bucket. Neither belongs
        // to this promotion's summary — the bucket belongs to no promotion at all.
        var sixth = db.SeedGroup(100, 1, rotationGroup: "A");
        sixth.LevelId = SixthYearId;
        db.AcademicGroups.Add(new AcademicGroup
        {
            Id = 999, Label = "Non réparti", GroupNumber = 0,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = null,
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetPromotionPartitioningQuery(TestHarness.LevelId, TestHarness.CurrentYearId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalGroups.Should().Be(6);
        result.Value.LabelledGroups.Should().Be(5);
        result.Value.UnlabelledGroups.Should().Be(1);
        result.Value.UnlabelledGroupNumbers.Should().Be("6");

        result.Value.Partitions.Select(p => (p.Label, p.GroupCount, p.GroupNumbers))
            .Should().Equal(("A", 3, "1-3"), ("B", 2, "4-5"));
    }

    [Fact]
    public async Task An_interleaved_cut_prints_its_holes()
    {
        // Contiguity in the printed cell comes from contiguity in the partition: an interleaved cut
        // genuinely cannot collapse, and merging across a hole would name rosters that are not there.
        await using var db = TestHarness.NewContext(nameof(An_interleaved_cut_prints_its_holes));
        db.SeedCatalog();

        for (int n = 1; n <= 6; n++)
            db.SeedGroup(n, n, rotationGroup: n % 2 == 1 ? "A" : "B");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetPromotionPartitioningQuery(TestHarness.LevelId, TestHarness.CurrentYearId), default);

        result.Value.Partitions.Select(p => p.GroupNumbers)
            .Should().Equal("1, 3, 5", "2, 4, 6");
    }

    [Fact]
    public async Task An_omitted_year_is_the_current_one_and_never_all_of_them()
    {
        await using var db = TestHarness.NewContext(nameof(An_omitted_year_is_the_current_one_and_never_all_of_them));
        db.SeedCatalog();
        var pastYear = db.SeedAcademicYear(
            2, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.SeedGroup(1, 1, rotationGroup: "A");
        db.SeedGroup(2, 2, rotationGroup: "B");
        db.SeedGroup(10, 1, rotationGroup: "A", academicYearId: pastYear.Id);
        db.SeedGroup(11, 2, rotationGroup: "B", academicYearId: pastYear.Id);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetPromotionPartitioningQuery(TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AcademicYearId.Should().Be(TestHarness.CurrentYearId);
        result.Value.TotalGroups.Should().Be(2, "last year's rosters are last year's promotion");
    }
}
