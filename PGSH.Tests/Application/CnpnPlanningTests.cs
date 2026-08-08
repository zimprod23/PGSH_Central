using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Planning under two concurrent CNPNs. From 2026-2027 one level holds students of both texts —
/// those arriving on the six-year CNPN and those repeating under the seven-year one — owing
/// different stage sets. Two rules keep that plannable: a group never mixes texts, and a group is
/// only given a cohort for a stage its own text requires.
/// </summary>
public class CnpnPlanningTests
{
    private const int OldText = TestHarness.OldCnpnId;
    private const int NewText = TestHarness.NewCnpnId;
    private const int SharedStage = TestHarness.StageId;   // required by both
    private const int OldOnlyStage = 77;                   // dropped by the new text

    private static Registration Enrol(ApplicationDbContext db, string first, string last, int? cnpnVersionId)
    {
        var registration = db.SeedRegistration(first, last);
        if (cnpnVersionId is { } id)
            registration.Student.AssignCnpnVersion(id, isInferred: false);
        return registration;
    }

    // ── Groups never mix texts ───────────────────────────────────────────────

    [Fact]
    public async Task Auto_arrange_never_puts_two_texts_in_one_group()
    {
        await using var db = TestHarness.NewContext("cnpn-groups-split");
        db.SeedCatalog();
        Enrol(db, "Sara", "Bennani", NewText);
        Enrol(db, "Ali", "Amrani", NewText);
        Enrol(db, "Nadia", "Idrissi", OldText);
        await db.SaveChangesAsync();

        // A size of 20 would fit all three in one group if the text were ignored.
        var result = await new AutoArrangeGroupsCommandHandler(db).Handle(
            new AutoArrangeGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId, 20), default);

        result.IsSuccess.Should().BeTrue();

        var placed = await db.Registrations
            .Include(r => r.Student)
            .Where(r => r.AcademicGroupId != null)
            .ToListAsync();

        var byGroup = placed
            .GroupBy(r => r.AcademicGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Student.CnpnVersionId).Distinct().ToList());

        byGroup.Should().HaveCount(2, "each text gets whole groups of its own");
        byGroup.Values.Should().OnlyContain(v => v.Count == 1);
    }

    [Fact]
    public async Task A_single_text_level_is_grouped_exactly_as_before()
    {
        await using var db = TestHarness.NewContext("cnpn-groups-single");
        db.SeedCatalog();
        for (int i = 0; i < 5; i++) Enrol(db, $"E{i}", "Test", NewText);
        await db.SaveChangesAsync();

        await new AutoArrangeGroupsCommandHandler(db).Handle(
            new AutoArrangeGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId, 20), default);

        var groups = await db.AcademicGroups.ToListAsync();
        groups.Should().ContainSingle();
        groups[0].Label.Should().NotContain("[",
            "the CNPN is only named when there is more than one to tell apart");
    }

    [Fact]
    public async Task Students_with_no_text_are_grouped_apart_rather_than_folded_in()
    {
        await using var db = TestHarness.NewContext("cnpn-groups-unstamped");
        db.SeedCatalog();
        Enrol(db, "Sara", "Bennani", NewText);
        Enrol(db, "Inconnu", "Sans-CNPN", null);
        await db.SaveChangesAsync();

        await new AutoArrangeGroupsCommandHandler(db).Handle(
            new AutoArrangeGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId, 20), default);

        var groups = await db.AcademicGroups.OrderBy(g => g.GroupNumber).ToListAsync();
        groups.Should().HaveCount(2);
        groups.Should().Contain(g => g.Label.Contains("CNPN à confirmer"),
            "an unassigned CNPN is a question for scolarité, not something to answer by guessing");
    }

    // ── Cohorts follow the text ──────────────────────────────────────────────

    /// <summary>
    /// One partition of new-text students, and two stages: one both texts require, one only the old
    /// text does.
    /// </summary>
    private static async Task SeedPlanningAsync(ApplicationDbContext db, int? studentText)
    {
        var stage = db.SeedCatalog();
        db.SeedStage(OldOnlyStage, "Stage supprimé par le nouveau texte");

        var group = new AcademicGroup
        {
            Id = 30, Label = "Groupe 30", GroupNumber = 30,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = TestHarness.LevelId,
            RotationGroup = "A",
        };
        db.AcademicGroups.Add(group);

        var registration = db.SeedRegistration("Sara", "Bennani", group);
        if (studentText is { } text)
            registration.Student.AssignCnpnVersion(text, isInferred: false);

        var oldSet = new Curriculum { Id = 1, LevelId = TestHarness.LevelId, CnpnVersionId = OldText };
        oldSet.AddStage(stage.Id, 2, 30);
        oldSet.AddStage(OldOnlyStage, 2, 30);

        var newSet = new Curriculum { Id = 2, LevelId = TestHarness.LevelId, CnpnVersionId = NewText };
        newSet.AddStage(stage.Id, 2, 30);

        db.Curriculums.AddRange(oldSet, newSet);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_group_gets_no_cohort_for_a_stage_its_text_dropped()
    {
        await using var db = TestHarness.NewContext("cnpn-cohort-refused");
        await SeedPlanningAsync(db, studentText: NewText);

        var result = await new CohortProvisioner(db).EnsureCohortsAsync(
            TestHarness.CurrentYearId,
            [("A", SharedStage), ("A", OldOnlyStage)],
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().Be(1, "only the stage the new text still requires");
        result.Value.NotRequiredByCnpn.Should().Be(1);

        (await db.Cohorts.SingleAsync()).StageId.Should().Be(SharedStage);
    }

    [Fact]
    public async Task The_same_stage_is_planned_for_a_group_whose_text_still_requires_it()
    {
        await using var db = TestHarness.NewContext("cnpn-cohort-allowed");
        await SeedPlanningAsync(db, studentText: OldText);

        var result = await new CohortProvisioner(db).EnsureCohortsAsync(
            TestHarness.CurrentYearId,
            [("A", SharedStage), ("A", OldOnlyStage)],
            default);

        result.Value.Created.Should().Be(2, "the old text requires both");
        result.Value.NotRequiredByCnpn.Should().Be(0);
    }

    [Fact]
    public async Task A_text_with_nothing_recorded_lets_planning_through()
    {
        // Arrêté 1650.25's requirements have not been entered yet. An enforcing check would block all
        // planning for six-year students on the strength of data nobody has typed in.
        await using var db = TestHarness.NewContext("cnpn-cohort-unrecorded");
        var stage = db.SeedCatalog();
        db.SeedStage(OldOnlyStage, "Stage hors texte");

        var group = new AcademicGroup
        {
            Id = 30, Label = "Groupe 30", GroupNumber = 30,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = TestHarness.LevelId,
            RotationGroup = "A",
        };
        db.AcademicGroups.Add(group);
        db.SeedRegistration("Sara", "Bennani", group).Student.AssignCnpnVersion(NewText, isInferred: false);
        await db.SaveChangesAsync();

        var result = await new CohortProvisioner(db).EnsureCohortsAsync(
            TestHarness.CurrentYearId, [("A", stage.Id), ("A", OldOnlyStage)], default);

        result.Value.Created.Should().Be(2);
        result.Value.NotRequiredByCnpn.Should().Be(0, "no requirement set means no opinion, not 'requires nothing'");
    }

    [Fact]
    public async Task A_group_whose_students_carry_no_text_is_left_to_the_level_rule()
    {
        // Never stamped, rather than stamped-then-stripped: the group simply cannot be checked
        // against any text.
        await using var db = TestHarness.NewContext("cnpn-cohort-unstamped");
        await SeedPlanningAsync(db, studentText: null);

        var result = await new CohortProvisioner(db).EnsureCohortsAsync(
            TestHarness.CurrentYearId, [("A", SharedStage), ("A", OldOnlyStage)], default);

        result.Value.Created.Should().Be(2);
        result.Value.NotRequiredByCnpn.Should().Be(0,
            "auto-arrange is where an unstamped group is prevented; reporting it here too would "
            + "surface the same problem twice");
    }
}
