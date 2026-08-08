using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// The CNPN is modelled as a per-(text, level) requirement set rather than a validity window on Stage.
// A window would force someone to know when a stage ends; nobody does — the text is reissued whenever
// the ministry decides, and a stage one text drops can come back in a later one. A set predicts nothing.
public class CurriculumTests
{
    private static Curriculum New(int levelId = 1, int cnpnVersionId = 1) =>
        new() { Id = 1, LevelId = levelId, CnpnVersionId = cnpnVersionId };

    [Fact]
    public void A_stage_can_be_required_with_the_weight_that_years_text_gave_it()
    {
        var curriculum = New();

        curriculum.AddStage(stageId: 7, coefficient: 3, durationInDays: 66).IsSuccess.Should().BeTrue();

        var entry = curriculum.Stages.Should().ContainSingle().Subject;
        entry.StageId.Should().Be(7);
        entry.Coefficient.Should().Be(3);
        entry.DurationInDays.Should().Be(66);
    }

    [Fact]
    public void The_same_stage_cannot_be_required_twice_in_one_year()
    {
        var curriculum = New();
        curriculum.AddStage(7, 1, 30);

        var result = curriculum.AddStage(7, 2, 60);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.StageAlreadyRequired(7));
        curriculum.Stages.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    public void A_coefficient_below_one_is_refused(int coefficient, int duration) =>
        New().AddStage(7, coefficient, duration).Error.Should().Be(CurriculumErrors.InvalidCoefficient);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public void A_duration_of_zero_or_less_is_refused(int coefficient, int duration) =>
        New().AddStage(7, coefficient, duration).Error.Should().Be(CurriculumErrors.InvalidDuration);

    [Fact]
    public void Dropping_a_stage_announces_it_because_failed_students_still_owe_it()
    {
        var curriculum = New();
        curriculum.AddStage(83, 2, 42);   // Pharmacie Clinique 3 — really removed in 2023-2024

        var result = curriculum.RemoveStage(83);

        result.IsSuccess.Should().BeTrue();
        curriculum.Stages.Should().BeEmpty();
        curriculum.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CurriculumStageRemovedDomainEvent>()
            .Which.StageId.Should().Be(83);
    }

    [Fact]
    public void Dropping_a_stage_that_was_never_required_is_refused()
    {
        var result = New().RemoveStage(83);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.StageNotRequired(83));
    }

    [Fact]
    public void A_year_is_seeded_from_the_previous_one_because_most_years_repeat()
    {
        var previous = New(levelId: 5, cnpnVersionId: 1);
        previous.AddStage(81, 2, 42);
        previous.AddStage(82, 2, 42);
        previous.AddStage(83, 2, 42);

        var current = New(levelId: 5, cnpnVersionId: 2);
        current.CopyFrom(previous).IsSuccess.Should().BeTrue();

        current.Stages.Should().HaveCount(3);
        current.Stages.Select(s => s.StageId).Should().BeEquivalentTo([81, 82, 83]);
        // Each year is an independent record, not a diff — the copy carries the weights across.
        current.Stages.Should().OnlyContain(s => s.Coefficient == 2 && s.DurationInDays == 42);
    }

    [Fact]
    public void A_stage_removed_one_year_can_come_back_later()
    {
        // The case a per-stage validity window cannot express at all.
        var y2022 = New(levelId: 5, cnpnVersionId: 1);
        y2022.AddStage(83, 2, 42);

        var y2023 = New(levelId: 5, cnpnVersionId: 2);
        y2023.CopyFrom(y2022);
        y2023.RemoveStage(83);

        var y2025 = New(levelId: 5, cnpnVersionId: 3);
        y2025.AddStage(83, 2, 42);

        y2022.Requires(83).Should().BeTrue();
        y2023.Requires(83).Should().BeFalse();
        y2025.Requires(83).Should().BeTrue();
    }

    [Fact]
    public void A_curriculum_is_only_copied_from_the_same_level()
    {
        var otherLevel = New(levelId: 4, cnpnVersionId: 1);
        otherLevel.AddStage(7, 1, 22);

        var result = New(levelId: 5, cnpnVersionId: 2).CopyFrom(otherLevel);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.LevelMismatch);
    }

    [Fact]
    public void Copying_onto_a_year_that_already_has_stages_is_refused()
    {
        var previous = New(levelId: 5, cnpnVersionId: 1);
        previous.AddStage(81, 2, 42);

        var current = New(levelId: 5, cnpnVersionId: 2);
        current.AddStage(82, 2, 42);

        var result = current.CopyFrom(previous);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.NotEmpty);
    }
}
