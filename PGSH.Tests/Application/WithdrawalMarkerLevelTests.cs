using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.Application.Stages.Levels.GetMany;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Retrait » is a status wearing a level's clothes, and every path that treats a level as a
/// promotion has to know it.
///
/// <para>The Access base used <c>CODE_N = 'MED00'</c> to mark a withdrawal rather than a year of
/// study, and <c>LegacyImport.LevelMapper</c> deliberately kept it as a level (year 0) so the
/// registration — and the rotations already served that year — survived the import. The data is
/// coherent: all 12 registrations are <c>Withdrawn</c>, the parcours read 1ère → 2ème → 3ème →
/// Retrait, and two of those students later came back. It is not repairable either, because MED00
/// <i>replaced</i> the real year in the source.</para>
///
/// <para>⚠ What it costs is that the marker is offered wherever a promotion is: it was selectable in
/// the planning pickers, and one of its rosters carried a partition label — an artefact of
/// <c>SplitAcademicGroupsPerLevel</c> copying the folded roster's label onto every shard.
/// <c>CnpnTargetPlanner</c> had already had to special-case year 0 by hand. These tests hold the
/// single rule (<see cref="Level.IsPromotion"/>) in place of a third hand-written exception.</para>
/// </summary>
public class WithdrawalMarkerLevelTests
{
    private const int RetraitId = 90;

    /// <summary>The catalogue plus the withdrawal marker and one roster sitting under it.</summary>
    private static AcademicGroup SeedRetrait(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedLevel(RetraitId, "Retrait", year: 0);

        var roster = db.SeedGroup(50, 59);
        roster.LevelId = RetraitId;
        return roster;
    }

    [Fact]
    public void Year_zero_is_not_a_promotion()
    {
        new Level { Year = 0, Label = "Retrait" }.IsPromotion.Should().BeFalse();
        new Level { Year = 1, Label = "Première Année" }.IsPromotion.Should().BeTrue();
    }

    [Fact]
    public async Task A_withdrawal_marker_cannot_be_cut_into_partitions()
    {
        await using var db = TestHarness.NewContext(nameof(A_withdrawal_marker_cannot_be_cut_into_partitions));
        SeedRetrait(db);
        await db.SaveChangesAsync();

        var result = await new AssignRotationGroupsCommandHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, RetraitId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotAPromotion");
        result.Error.Description.Should().Contain("Retrait");

        (await db.AcademicGroups.Where(g => g.LevelId == RetraitId).ToListAsync())
            .Should().OnlyContain(g => g.RotationGroup == null);
    }

    [Fact]
    public async Task A_withdrawal_marker_cannot_be_arranged_into_groups()
    {
        await using var db = TestHarness.NewContext(nameof(A_withdrawal_marker_cannot_be_arranged_into_groups));
        db.SeedCatalog();
        db.SeedLevel(RetraitId, "Retrait", year: 0);
        db.SeedRegistration("Parti", "Étudiant", levelId: RetraitId);
        await db.SaveChangesAsync();

        var result = await new AutoArrangeGroupsCommandHandler(db).Handle(
            new AutoArrangeGroupsCommand(RetraitId, TestHarness.CurrentYearId, GroupSize: 20), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotAPromotion");

        (await db.AcademicGroups.CountAsync(g => g.LevelId == RetraitId)).Should().Be(0);
    }

    /// <summary>
    /// ⚠ The clear is deliberately <b>not</b> guarded. A label already on a marker's roster is exactly
    /// what has to be taken back off, and refusing the undo because the state should not exist leaves
    /// no way to reach it but SQL.
    /// </summary>
    [Fact]
    public async Task But_a_label_already_on_one_can_still_be_cleared()
    {
        await using var db = TestHarness.NewContext(nameof(But_a_label_already_on_one_can_still_be_cleared));
        var roster = SeedRetrait(db);
        roster.RotationGroup = "E";
        await db.SaveChangesAsync();

        var result = await new ClearRotationGroupsCommandHandler(db).Handle(
            new ClearRotationGroupsCommand(TestHarness.CurrentYearId, RetraitId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cleared.Should().Be(1);

        (await db.AcademicGroups.FirstAsync(g => g.Id == roster.Id))
            .RotationGroup.Should().BeNull();
    }

    [Fact]
    public async Task Promotions_only_hides_the_marker_and_nothing_else()
    {
        await using var db = TestHarness.NewContext(nameof(Promotions_only_hides_the_marker_and_nothing_else));
        db.SeedCatalog();
        db.SeedLevel(RetraitId, "Retrait", year: 0);
        db.SeedLevel(91, "Sixième Année Médecine", year: 6);
        await db.SaveChangesAsync();

        var all = await new GetLevelsQueryHandler(db).Handle(
            new GetLevelsQuery(null, null, PageSize: 50), default);
        var promotions = await new GetLevelsQueryHandler(db).Handle(
            new GetLevelsQuery(null, null, PageSize: 50, PromotionsOnly: true), default);

        all.Value.Items.Should().Contain(l => l.Label == "Retrait",
            "the dossier and the parcours have to be able to name a withdrawn registration's level");
        promotions.Value.Items.Should().NotContain(l => l.Label == "Retrait");
        promotions.Value.TotalCount.Should().Be(all.Value.TotalCount - 1,
            "exactly one level is a marker — the filter must not take a promotion with it");
    }

    /// <summary>
    /// The handler filters on <c>Year > 0</c> because an unmapped computed property cannot be
    /// translated to SQL. If the domain rule ever moves, this is what fails.
    /// </summary>
    [Fact]
    public async Task The_sql_predicate_and_the_domain_rule_agree()
    {
        await using var db = TestHarness.NewContext(nameof(The_sql_predicate_and_the_domain_rule_agree));
        db.SeedCatalog();
        db.SeedLevel(RetraitId, "Retrait", year: 0);
        db.SeedLevel(91, "Sixième Année Médecine", year: 6);
        await db.SaveChangesAsync();

        var kept = await new GetLevelsQueryHandler(db).Handle(
            new GetLevelsQuery(null, null, PageSize: 50, PromotionsOnly: true), default);
        var keptIds = kept.Value.Items.Select(l => l.Id).ToHashSet();

        foreach (var level in await db.Levels.ToListAsync())
            keptIds.Contains(level.Id).Should().Be(level.IsPromotion,
                $"« {level.Label} » must be kept by the query exactly when the domain calls it a promotion");
    }
}
