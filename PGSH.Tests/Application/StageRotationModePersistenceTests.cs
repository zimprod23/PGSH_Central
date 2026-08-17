using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Update;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The mode has to survive an ordinary edit of the stage.
///
/// <para>⚠ Caught in the smoke test, not by the suite: the PUT endpoint builds
/// <see cref="UpdateStageCommand"/> from its own inner <c>Request</c> record, and that record did not
/// carry <c>RotationMode</c>. A PUT re-states the whole stage, so the missing field was not "left
/// alone" — it arrived as the parameter's default and wrote <c>PerPeriod</c> back over the row on
/// every save. The command's parameter is no longer optional, so the compiler now refuses a caller
/// that forgets it; these tests cover the behaviour the compiler cannot.</para>
/// </summary>
public class StageRotationModePersistenceTests
{
    private static UpdateStageCommand Edit(Stage stage, StageRotationMode mode) =>
        new(stage.Id, stage.Name, stage.Coefficient, stage.Description,
            stage.DurationInDays, stage.LevelId, [], mode);

    [Fact]
    public async Task Switching_a_stage_to_single_service_persists()
    {
        await using var db = TestHarness.NewContext(nameof(Switching_a_stage_to_single_service_persists));
        var stage = db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await new UpdateStageCommandHandler(db)
            .Handle(Edit(stage, StageRotationMode.SingleService), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Stages.SingleAsync(s => s.Id == stage.Id)).RotationMode
            .Should().Be(StageRotationMode.SingleService);
    }

    [Fact]
    public async Task Editing_an_unrelated_field_does_not_revert_the_mode()
    {
        // The exact shape of the bug: rename the stage, say nothing new about the mode, and watch the
        // mode go back to the default.
        await using var db = TestHarness.NewContext(nameof(Editing_an_unrelated_field_does_not_revert_the_mode));
        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;
        await db.SaveChangesAsync();

        var rename = Edit(stage, StageRotationMode.SingleService) with { Name = "Gynécologie Obstétrique" };
        await new UpdateStageCommandHandler(db).Handle(rename, default);

        var after = await db.Stages.SingleAsync(s => s.Id == stage.Id);
        after.Name.Should().Be("Gynécologie Obstétrique");
        after.RotationMode.Should().Be(StageRotationMode.SingleService);
    }

    [Fact]
    public async Task Switching_back_to_per_period_persists_too()
    {
        await using var db = TestHarness.NewContext(nameof(Switching_back_to_per_period_persists_too));
        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;
        await db.SaveChangesAsync();

        await new UpdateStageCommandHandler(db)
            .Handle(Edit(stage, StageRotationMode.PerPeriod), default);

        (await db.Stages.SingleAsync(s => s.Id == stage.Id)).RotationMode
            .Should().Be(StageRotationMode.PerPeriod);
    }
}
