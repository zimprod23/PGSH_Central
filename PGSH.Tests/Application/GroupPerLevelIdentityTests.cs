using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.AcademicGroups.Create;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.Application.AcademicGroups.Transfer;
using PGSH.Application.AcademicGroups.Update;
using PGSH.Application.Stages.Cohorts.Create;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;

namespace PGSH.Tests.Application;

/// <summary>
/// A roster belongs to one promotion, and its number only means anything alongside that promotion.
///
/// <para>The faculty numbers its groups per promotion and runs them concurrently — the 3rd year 1-80,
/// the 5th year 1-60, the 6th year 1-100 — but the base keyed them on (year, number) alone, so the
/// three numberings were folded into one set of rows. Measured 2026-08-13 on the real base: 80 of the
/// 100 numbered rosters of 2025-2026 carried registrations from four or five promotions at once, and
/// <c>LevelId</c> was null on all 1,003 rows.</para>
///
/// <para>⚠ That is the defect that emptied a répartition. <c>GroupScheduleConflictGuard</c> forbids a
/// roster from being in two services at once — correctly, on the premise that a roster is one set of
/// students. With the rows shared, the 3rd year's April–July placements <em>were</em> the 5th year's,
/// so seven of the 5th year's nine columns were refused and the printed document had two.
/// <c>RotationGroup</c> was shared the same way: one global cut per year, which re-cutting any one
/// promotion silently re-cut for all the others.</para>
///
/// <para>⚠ <b>The uniqueness of (year, promotion, number) is not covered here.</b>
/// <c>UseInMemoryDatabase</c> ignores unique indexes, so <c>IX_AcademicGroup_Year_Level_Number</c> —
/// and its <c>NULLS NOT DISTINCT</c>, which is what keeps the year's single « Non réparti » from being
/// duplicated — can only be exercised against a real PostgreSQL. What is covered here is the
/// behaviour that has to hold on top of it.</para>
/// </summary>
public class GroupPerLevelIdentityTests
{
    private const int SixthYearId = 60;

    private static void SeedTwoPromotions(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedLevel(SixthYearId, "6ème année", 6, AcademicProgram.Medecine);
    }

    [Fact]
    public async Task Auto_arrange_numbers_each_promotion_from_one()
    {
        // The number is printed on the répartition, and the faculty's document opens at 1 for every
        // promotion. Numbering across the year instead handed the 6th year groups 3, 4, 5… purely
        // because the 3rd year had been arranged first.
        await using var db = TestHarness.NewContext(nameof(Auto_arrange_numbers_each_promotion_from_one));
        SeedTwoPromotions(db);

        for (int i = 0; i < 4; i++) db.SeedRegistration($"T{i}", "Troisieme");
        for (int i = 0; i < 4; i++)
            db.SeedRegistration($"S{i}", "Sixieme", group: null,
                academicYearId: TestHarness.CurrentYearId, levelId: SixthYearId);
        await db.SaveChangesAsync();

        var handler = new AutoArrangeGroupsCommandHandler(db);

        await handler.Handle(
            new AutoArrangeGroupsCommand(TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: 2), default);
        await handler.Handle(
            new AutoArrangeGroupsCommand(SixthYearId, TestHarness.CurrentYearId, GroupSize: 2), default);

        var byLevel = await db.AcademicGroups
            .GroupBy(g => g.LevelId)
            .Select(g => new { Level = g.Key, Numbers = g.Select(x => x.GroupNumber).ToList() })
            .ToListAsync();

        byLevel.Should().HaveCount(2);
        byLevel.Should().OnlyContain(x => x.Numbers.Count == 2);
        foreach (var level in byLevel)
            level.Numbers.Order().Should().Equal([1, 2], "each promotion counts its own rosters");
    }

    [Fact]
    public async Task Auto_arrange_stamps_the_promotion_on_every_roster_it_creates()
    {
        await using var db = TestHarness.NewContext(nameof(Auto_arrange_stamps_the_promotion_on_every_roster_it_creates));
        SeedTwoPromotions(db);
        for (int i = 0; i < 3; i++) db.SeedRegistration($"T{i}", "Troisieme");
        await db.SaveChangesAsync();

        await new AutoArrangeGroupsCommandHandler(db).Handle(
            new AutoArrangeGroupsCommand(TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: 2), default);

        (await db.AcademicGroups.ToListAsync())
            .Should().OnlyContain(g => g.LevelId == TestHarness.LevelId);
    }

    [Fact]
    public async Task Cutting_one_promotion_leaves_another_untouched()
    {
        // The consequence of the shared row, in the form it was actually seen: the 5th year was cut
        // into 9 partitions and the 6th year's labels came out A=19, B=19 … J=2 — a mixture of two
        // promotions' cuts on one set of rows, under a legend claiming to describe one.
        await using var db = TestHarness.NewContext(nameof(Cutting_one_promotion_leaves_another_untouched));
        SeedTwoPromotions(db);

        for (int i = 1; i <= 4; i++) db.SeedGroup(groupId: i, groupNumber: i);
        for (int i = 1; i <= 4; i++)
        {
            var sixth = db.SeedGroup(groupId: 100 + i, groupNumber: i);
            sixth.LevelId = SixthYearId;
        }
        await db.SaveChangesAsync();

        await new AssignRotationGroupsCommandHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        var sixthYear = await db.AcademicGroups.Where(g => g.LevelId == SixthYearId).ToListAsync();
        sixthYear.Should().OnlyContain(g => g.RotationGroup == null, "only the 3rd year was cut");

        var thirdYear = await db.AcademicGroups.Where(g => g.LevelId == TestHarness.LevelId).ToListAsync();
        thirdYear.Should().OnlyContain(g => g.RotationGroup != null);
    }

    [Fact]
    public async Task The_no_promotion_bucket_is_never_given_a_partition_label()
    {
        // « Non réparti » holds every promotion's unassigned students — 4,725 of them in 2025-2026 —
        // and is the one roster that legitimately has no level. Reaching it through "has a
        // registration at this level" made it a member of every promotion's cut at once.
        await using var db = TestHarness.NewContext(nameof(The_no_promotion_bucket_is_never_given_a_partition_label));
        db.SeedCatalog();

        for (int i = 1; i <= 4; i++) db.SeedGroup(groupId: i, groupNumber: i);

        var bucket = new AcademicGroup
        {
            Id = 999, Label = "Non réparti", GroupNumber = 0,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = null,
        };
        db.AcademicGroups.Add(bucket);
        db.SeedRegistration("Sans", "Groupe", bucket);
        await db.SaveChangesAsync();

        var result = await new AssignRotationGroupsCommandHandler(db).Handle(
            new AssignRotationGroupsCommand(TestHarness.CurrentYearId, 2, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalGroups.Should().Be(4, "the four rosters of the promotion, and not the bucket");
        result.Value.Labeled.Should().Be(4);

        (await db.AcademicGroups.FirstAsync(g => g.Id == 999))
            .RotationGroup.Should().BeNull();
    }

    // ─── The write paths that could rebuild the defect by hand ──────────────────────────────────
    //
    // SplitAcademicGroupsPerLevel repaired the data and IX_AcademicGroup_Year_Level_Number keeps two
    // rosters distinguishable. Neither stops a *student* from being moved into another promotion's
    // roster, or a *cohorte* from being built across two promotions: both are ordinary FKs to rows
    // that exist. These are those paths.

    private static TransferStudentCommandHandler TransferHandler(ApplicationDbContext db) =>
        new(db, new MidStageTransferRescheduler(db));

    [Fact]
    public async Task A_student_cannot_be_transferred_into_another_promotions_roster()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_cannot_be_transferred_into_another_promotions_roster));
        SeedTwoPromotions(db);

        var home  = db.SeedGroup(groupId: 1, groupNumber: 1);
        var other = db.SeedGroup(groupId: 2, groupNumber: 1);
        other.LevelId = SixthYearId;

        var registration = db.SeedRegistration("Imane", "Chraibi", home);
        await db.SaveChangesAsync();

        var result = await TransferHandler(db).Handle(new TransferStudentCommand(
            registration.Id, other.Id, "Erreur de saisie", TransferType.Definitive), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.TargetGroupInAnotherLevel");
        (await db.Registrations.FirstAsync(r => r.Id == registration.Id))
            .AcademicGroupId.Should().Be(home.Id, "the move never happened");
    }

    [Fact]
    public async Task A_student_cannot_be_transferred_into_another_years_roster()
    {
        // The subtler half: same promotion, wrong année. A registration *is* a year, so pointing it
        // at last year's roster does not move the student — it makes the row describe two years.
        await using var db = TestHarness.NewContext(nameof(A_student_cannot_be_transferred_into_another_years_roster));
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var home = db.SeedGroup(groupId: 1, groupNumber: 1);
        var lastYear = db.SeedGroup(groupId: 2, groupNumber: 1, academicYearId: TestHarness.PreviousYearId);

        var registration = db.SeedRegistration("Imane", "Chraibi", home);
        await db.SaveChangesAsync();

        var result = await TransferHandler(db).Handle(new TransferStudentCommand(
            registration.Id, lastYear.Id, "Erreur de saisie", TransferType.Definitive), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.TargetGroupInAnotherYear");
    }

    [Fact]
    public async Task A_cohort_cannot_pair_a_roster_with_another_promotions_stage()
    {
        // CohortProvisioner has always checked this on the bulk path; the hand-built one had no
        // equivalent, so a cohorte across promotions was one POST away — and a cell's level is read
        // off Cohort.Stage.LevelId, so it would have been booked against the wrong promotion's quota.
        await using var db = TestHarness.NewContext(nameof(A_cohort_cannot_pair_a_roster_with_another_promotions_stage));
        SeedTwoPromotions(db);
        db.SeedStage(stageId: 60, name: "Urgences", levelId: SixthYearId);

        var thirdYearRoster = db.SeedGroup(groupId: 1, groupNumber: 1);
        await db.SaveChangesAsync();

        var result = await new CreateCohortCommandHandler(db).Handle(
            new CreateCohortCommand(60, thirdYearRoster.Id, "Urgences · G1"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cohorts.PromotionMismatch");
    }

    [Fact]
    public async Task A_cohort_cannot_be_built_on_the_no_promotion_bucket()
    {
        await using var db = TestHarness.NewContext(nameof(A_cohort_cannot_be_built_on_the_no_promotion_bucket));
        var stage = db.SeedCatalog();

        var bucket = new AcademicGroup
        {
            Id = 999, Label = "Non réparti", GroupNumber = 0,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = null,
        };
        db.AcademicGroups.Add(bucket);
        await db.SaveChangesAsync();

        var result = await new CreateCohortCommandHandler(db).Handle(
            new CreateCohortCommand(stage.Id, bucket.Id, "Cardiologie · Non réparti"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cohorts.OnUnassignedRoster");
    }

    [Fact]
    public async Task The_bucket_cannot_be_handed_a_partition_label_by_hand()
    {
        // AssignRotationGroups can no longer reach it, but Update writes RotationGroup directly — and
        // a labelled bucket is all CohortProvisioner needs to give 4,725 people a cohorte.
        await using var db = TestHarness.NewContext(nameof(The_bucket_cannot_be_handed_a_partition_label_by_hand));
        db.SeedCatalog();

        var bucket = new AcademicGroup
        {
            Id = 999, Label = "Non réparti", GroupNumber = 0,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = null,
        };
        db.AcademicGroups.Add(bucket);
        await db.SaveChangesAsync();

        var result = await new UpdateGroupCommandHandler(db).Handle(
            new UpdateGroupCommand(bucket.Id, "Non réparti", null, "A"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.UnassignedRosterCannotBePartitioned");
        (await db.AcademicGroups.FirstAsync(g => g.Id == 999)).RotationGroup.Should().BeNull();
    }

    [Fact]
    public async Task Two_promotions_can_both_have_a_Groupe_1()
    {
        // The label is what an admin reads, so it has to distinguish two rosters *of one promotion* —
        // and only that. Held to (year, label), the obvious name for the 6th year's first roster was
        // already taken by the 3rd year's, so a promotion could not be named the way it is printed.
        await using var db = TestHarness.NewContext(nameof(Two_promotions_can_both_have_a_Groupe_1));
        SeedTwoPromotions(db);
        await db.SaveChangesAsync();

        var handler = new CreateGroupCommandHandler(db);

        var third = await handler.Handle(
            new CreateGroupCommand("Groupe 1", TestHarness.CurrentYearId, TestHarness.LevelId, null, null), default);
        var sixth = await handler.Handle(
            new CreateGroupCommand("Groupe 1", TestHarness.CurrentYearId, SixthYearId, null, null), default);

        third.IsSuccess.Should().BeTrue();
        sixth.IsSuccess.Should().BeTrue("« Groupe 1 » names one roster per promotion, not one per year");

        var numbers = await db.AcademicGroups.Select(g => new { g.LevelId, g.GroupNumber }).ToListAsync();
        numbers.Should().OnlyContain(g => g.GroupNumber == 1, "each promotion counts from 1");

        // …and the same promotion still cannot hold the name twice.
        var duplicate = await handler.Handle(
            new CreateGroupCommand("Groupe 1", TestHarness.CurrentYearId, TestHarness.LevelId, null, null), default);
        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Code.Should().Be("AcademicGroups.DuplicateLabel");
    }

    [Fact]
    public async Task Provisioning_never_reaches_a_roster_of_another_promotion()
    {
        // The partition label is a plain string, so « A » in the 3rd year and « A » in the 6th are
        // indistinguishable to a plan naming partitions. The promotion is what separates them.
        await using var db = TestHarness.NewContext(nameof(Provisioning_never_reaches_a_roster_of_another_promotion));
        var thirdYearStage = SeedTwoPromotionsWithStage(db);

        db.SeedGroup(groupId: 1, groupNumber: 1, rotationGroup: "A");
        var sixth = db.SeedGroup(groupId: 2, groupNumber: 1, rotationGroup: "A");
        sixth.LevelId = SixthYearId;
        await db.SaveChangesAsync();

        var result = await new CohortProvisioner(db).EnsureCohortsAsync(
            TestHarness.CurrentYearId, [("A", thirdYearStage.Id)], default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().Be(1);
        (await db.Cohorts.Select(c => c.AcademicGroupId).ToListAsync())
            .Should().Equal([1], "only the 3rd year's « A » owes a 3rd-year stage");
    }

    private static Stage SeedTwoPromotionsWithStage(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        db.SeedLevel(SixthYearId, "6ème année", 6, AcademicProgram.Medecine);
        return stage;
    }
}
