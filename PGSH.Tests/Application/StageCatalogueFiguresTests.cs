using FluentAssertions;
using PGSH.Application.Stages.GetMany;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The catalogue's coefficient and duration are duplicated by every text's
/// <c>CurriculumStage</c>, and since arrêté 1650.25 landed they no longer agree. The Stages page
/// rendered the catalogue number alone, so it asserted a figure no CNPN necessarily states.
///
/// <para>⚠ <b>Neither number is wrong.</b> A 5ᵉ année student revalidating a 3ᵉ année credit is
/// still governed by 2174.18, so 66 j.o. has to remain readable after the catalogue moved to 30 —
/// which is exactly what the alignment migration preserved. What the row has to carry is *which
/// text says what*, so the screen can stop presenting one of them as the answer.</para>
/// </summary>
public class StageCatalogueFiguresTests
{
    private const int OldText = TestHarness.OldCnpnId;
    private const int NewText = TestHarness.NewCnpnId;

    private static async Task<ApplicationDbContext> SeedAsync(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();

        // The live shape after Cnpn1650Med3CatalogueAlignment: the catalogue moved to the new text's
        // figures, and the previous text kept its own.
        stage.Coefficient = 3;
        stage.DurationInDays = 30;

        var oldSet = new Curriculum { Id = 1, LevelId = TestHarness.LevelId, CnpnVersionId = OldText };
        oldSet.AddStage(stage.Id, 3, 66);

        var newSet = new Curriculum { Id = 2, LevelId = TestHarness.LevelId, CnpnVersionId = NewText };
        newSet.AddStage(stage.Id, 1, 30);

        db.Curriculums.AddRange(oldSet, newSet);
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<StageSummaryResponse> SingleRowAsync(ApplicationDbContext db)
    {
        var result = await new GetStagesQueryHandler(db).Handle(
            new GetStagesQuery(SearchTerm: null, LevelId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value.Items.Single(s => s.Id == TestHarness.StageId);
    }

    [Fact]
    public async Task A_stage_row_carries_every_texts_own_figures()
    {
        await using var db = await SeedAsync("stage-figures-carried");

        var row = await SingleRowAsync(db);

        row.TextFigures.Should().HaveCount(2);
        row.TextFigures.Should().ContainSingle(f => f.CnpnCode == "2174.18")
            .Which.Should().BeEquivalentTo(new { Coefficient = 3, DurationInDays = 66 });
        row.TextFigures.Should().ContainSingle(f => f.CnpnCode == "1650.25")
            .Which.Should().BeEquivalentTo(new { Coefficient = 1, DurationInDays = 30 });
    }

    [Fact]
    public async Task The_catalogue_figures_are_still_reported_unchanged()
    {
        await using var db = await SeedAsync("stage-figures-catalogue");

        var row = await SingleRowAsync(db);

        // The page keeps showing the catalogue value — it is what the edit form writes back. The
        // change is that it is no longer the only number on the row.
        row.Coefficient.Should().Be(3);
        row.DurationInDays.Should().Be(30);
    }

    [Fact]
    public async Task A_stage_no_text_states_carries_no_figures_rather_than_a_zero()
    {
        await using var db = await SeedAsync("stage-figures-absent");
        db.SeedStage(stageId: 900, name: "Stage hors CNPN", coefficient: 4);
        await db.SaveChangesAsync();

        var result = await new GetStagesQueryHandler(db).Handle(
            new GetStagesQuery(SearchTerm: null, LevelId: null), CancellationToken.None);

        var orphan = result.Value.Items.Single(s => s.Id == 900);

        // Empty, never a fabricated row: « aucun texte ne le mentionne » and « un texte dit 0 » are
        // different statements, and the screen has to be able to tell them apart.
        orphan.TextFigures.Should().BeEmpty();
        orphan.Coefficient.Should().Be(4);
    }

    [Fact]
    public async Task The_figures_follow_the_page_and_not_the_whole_catalogue()
    {
        await using var db = await SeedAsync("stage-figures-paged");
        for (int i = 0; i < 5; i++)
            db.SeedStage(stageId: 910 + i, name: $"Zzz stage {i}");
        await db.SaveChangesAsync();

        var result = await new GetStagesQueryHandler(db).Handle(
            new GetStagesQuery(SearchTerm: null, LevelId: null, PageNumber: 1, PageSize: 1),
            CancellationToken.None);

        // One row on the page, and the second query is keyed on that row alone.
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].TextFigures.Should().NotBeEmpty();
    }
}
