using FluentAssertions;
using PGSH.Application.AcademicGroups.DeleteAll;
using PGSH.Application.AcademicGroups.Empty;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Cohorts.Delete;
using PGSH.Application.Stages.Cohorts.DeleteAll;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// What the teardown buttons cost, and what they are allowed to take with them.
//
// The defect these cover: « Vider le groupe » cleared Registration.AcademicGroupId and nothing else,
// while an InternshipAssignment hangs off the COHORTE. So every affectation survived the act — still
// on the chefs' worklists, still counted in the services' occupancy, still in the printed
// répartition — against a roster displaying 0 étudiants, with nothing on either screen saying the two
// disagreed. And « Supprimer la cohorte » had no guard at all, while its bulk twin refused as soon as
// one affectation had left Planned: the safe act was the one touching a hundred cohortes, and the
// unguarded one was the button beside each line.
//
// ⚠ The success path of the two bulk handlers is deliberately uncovered: both use
// ExecuteUpdate/ExecuteDelete, which the in-memory provider does not support. Their refusals are pure
// reads and are covered — which is the half that had no guard at all.
public class RosterTeardownGuardTests
{
    private const int ServiceId = 1;
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private sealed record World(ApplicationDbContext Db, Cohort Cohort, InternshipAssignment Assignment);

    /// <summary>One roster, one cohorte, one student affected to it — the ordinary planned state.</summary>
    private static World Seed(string name, bool started = false)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Yasmine", "Alami", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, service, Start, End, started: false);
        db.SaveChanges();

        if (started)
        {
            // Through the real lifecycle: a period pre-set to started leaves the assignment Planned,
            // and the guard reads both.
            assignment.Start();
            db.SaveChanges();
        }

        return new World(db, cohort, assignment);
    }

    private static EmptyGroupCommandHandler EmptyHandler(ApplicationDbContext db) =>
        new(db, new AffectationTollReader(db));

    private static DeleteCohortCommandHandler DeleteHandler(ApplicationDbContext db) =>
        new(db, new AffectationTollReader(db), new RecordingAuditTrail());

    private static DeleteAllCohortsCommandHandler ResetHandler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new AffectationTollReader(db), new RecordingAuditTrail());

    // --- « Vider le groupe » --------------------------------------------------

    [Fact]
    public async Task Emptying_a_roster_that_holds_affectations_is_refused_and_writes_nothing()
    {
        var world = Seed(nameof(Emptying_a_roster_that_holds_affectations_is_refused_and_writes_nothing));

        var result = await EmptyHandler(world.Db)
            .Handle(new EmptyGroupCommand(world.Cohort.AcademicGroupId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.RosterHasAffectations");
        // The refusal names the numbers: a count nobody is shown is a count nobody can consent to.
        result.Error.Description.Should().Contain("1 affectation").And.Contain("1 période");

        // ⚠ The assertion the test exists for: a guard ordered after the write returns the same
        // failure and satisfies every assertion about the failure. Only the store tells them apart.
        world.Db.Registrations.Single().AcademicGroupId.Should().Be(world.Cohort.AcademicGroupId);
        world.Db.InternshipAssignments.Should().ContainSingle();
        world.Db.ServicePeriods.Should().ContainSingle();
    }

    [Fact]
    public async Task Emptying_a_roster_with_DropAffectations_removes_them_and_says_how_many()
    {
        var world = Seed(nameof(Emptying_a_roster_with_DropAffectations_removes_them_and_says_how_many));

        var result = await EmptyHandler(world.Db)
            .Handle(new EmptyGroupCommand(world.Cohort.AcademicGroupId, DropAffectations: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Unassigned.Should().Be(1);
        result.Value.AffectationsRemoved.Should().Be(1);
        result.Value.PeriodsRemoved.Should().Be(1);

        world.Db.Registrations.Single().AcademicGroupId.Should().BeNull();
        world.Db.InternshipAssignments.Should().BeEmpty();
        world.Db.ServicePeriods.Should().BeEmpty();
        world.Db.CohortMembership.Should().BeEmpty();
        // The cohorte is a structural row and is not a roster-side act's to delete.
        world.Db.Cohorts.Should().ContainSingle();
    }

    [Fact]
    public async Task Emptying_a_roster_whose_rotation_started_is_refused_even_with_DropAffectations()
    {
        var world = Seed(
            nameof(Emptying_a_roster_whose_rotation_started_is_refused_even_with_DropAffectations),
            started: true);

        var result = await EmptyHandler(world.Db)
            .Handle(new EmptyGroupCommand(world.Cohort.AcademicGroupId, DropAffectations: true), default);

        // Not forceable from here: the act that destroys marks and attendance is « Dépublier », which
        // names what it costs and asks twice. A roster-side button must not become the way round it.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.RosterAffectationsUnderway");
        result.Error.Description.Should().Contain("1 ont démarré");

        world.Db.Registrations.Single().AcademicGroupId.Should().Be(world.Cohort.AcademicGroupId);
        world.Db.InternshipAssignments.Should().ContainSingle();
        world.Db.ServicePeriods.Should().ContainSingle();
    }

    [Fact]
    public async Task Emptying_a_roster_that_holds_no_affectation_still_works_unasked()
    {
        // The control. A path that refuses everything satisfies every refusal assertion and proves
        // nothing — and « Non réparti », which carries no cohorte, is emptied as a matter of routine.
        var db = TestHarness.NewContext(nameof(Emptying_a_roster_that_holds_no_affectation_still_works_unasked));
        db.SeedCatalog();
        var bucket = db.SeedUnassignedBucket(99);
        db.SeedRegistration("Omar", "Benali", bucket);
        db.SeedRegistration("Salma", "Idrissi", bucket);
        db.SaveChanges();

        var result = await EmptyHandler(db).Handle(new EmptyGroupCommand(bucket.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Unassigned.Should().Be(2);
        result.Value.AffectationsRemoved.Should().Be(0);
        db.Registrations.Should().OnlyContain(r => r.AcademicGroupId == null);
    }

    [Fact]
    public async Task Emptying_every_roster_of_a_year_is_refused_while_any_affectation_exists()
    {
        var world = Seed(nameof(Emptying_every_roster_of_a_year_is_refused_while_any_affectation_exists));

        var result = await new EmptyAllYearGroupsCommandHandler(
                world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail())
            .Handle(new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId), default);

        // No DropAffectations on this one, on purpose: a year's affectations are the whole faculty's
        // planning, and destroying them is not what anybody means by « retirer les étudiants ».
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.YearRostersHaveAffectations");
        world.Db.Registrations.Single().AcademicGroupId.Should().NotBeNull();
    }

    [Fact]
    public async Task Emptying_one_promotion_is_refused_only_over_that_promotions_affectations()
    {
        // The defect: « Vider » had no scope between one roster and the whole year. A promotion whose
        // planning had been cleared could not be re-decoupee until every OTHER promotion of the year
        // had been cleared too — measured on the live base 2026-09-07, a 4e annee Medecine holding no
        // affectation at all was refused over 11 916 belonging to three other promotions.
        var world = Seed(nameof(Emptying_one_promotion_is_refused_only_over_that_promotions_affectations));

        // A second promotion in the same year, with rosters and students but no planning at all.
        var otherLevel = new Level
        {
            Id = 77, Label = "4ème année", Year = 4, AcademicProgram = AcademicProgram.Medecine,
        };
        world.Db.Levels.Add(otherLevel);
        var otherRoster = world.Db.SeedGroup(501, 1, levelId: otherLevel.Id);
        world.Db.SeedRegistration("Nabil", "Cherkaoui", otherRoster, levelId: otherLevel.Id);
        world.Db.SaveChanges();

        var handler = new EmptyAllYearGroupsCommandHandler(world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail());

        // Year-wide, the toll is the whole faculty's: still refused, and rightly so.
        var yearWide = await handler.Handle(
            new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId), default);
        yearWide.IsFailure.Should().BeTrue();
        yearWide.Error.Code.Should().Be("AcademicGroups.YearRostersHaveAffectations");

        // Scoped to the promotion that IS planned, the refusal names it rather than the year.
        var planned = await handler.Handle(
            new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);
        planned.IsFailure.Should().BeTrue();
        planned.Error.Code.Should().Be("AcademicGroups.PromotionRostersHaveAffectations");
        planned.Error.Description.Should().Contain("3ème année")
            .And.Contain("1 affectation").And.Contain("1 période");

        // And scoped to the promotion that holds none, it is not refused — which is the whole point.
        //
        // ⚠ Asserted as « it reached the write », not as a Result. The clearing statement is
        // ExecuteUpdate, which the in-memory provider refuses outright, so getting that far IS the
        // observable fact that the guard let the act through — and it is the same statement the
        // year-wide path has always run. Written as an IsSuccess assertion this case would be
        // unreachable, and the scoping it covers would go untested.
        var clean = async () => await handler.Handle(
            new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId, otherLevel.Id), default);

        (await clean.Should().ThrowAsync<InvalidOperationException>(
                "the promotion holds no affectation, so the guard passes and the write is attempted"))
            .WithMessage("*ExecuteUpdate*");
    }

    [Fact]
    public async Task Emptying_an_unknown_promotion_is_refused_rather_than_widened_to_the_year()
    {
        // A level id that resolves to nothing must not fall through to « no level named » — that is
        // the widening-on-absence defect, on the one act here that writes across a whole year.
        var world = Seed(nameof(Emptying_an_unknown_promotion_is_refused_rather_than_widened_to_the_year));

        var result = await new EmptyAllYearGroupsCommandHandler(
                world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail())
            .Handle(new EmptyAllYearGroupsCommand(TestHarness.CurrentYearId, 4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
        world.Db.Registrations.Single().AcademicGroupId.Should().NotBeNull();
    }

    // --- « Supprimer les groupes » --------------------------------------------

    [Fact]
    public async Task Deleting_one_promotions_rosters_is_refused_only_over_that_promotions_students()
    {
        // The defect, reported 2026-09-07: « quand j'ai vidé tous les groupes de TOUTES les promotions
        // et réessayé, ça a marché ». Deleting was the last roster act with no scope between one
        // roster and the whole year, so a promotion whose rosters were empty was refused over another
        // promotion's students — and the only way through was to empty the entire year.
        var world = Seed(nameof(Deleting_one_promotions_rosters_is_refused_only_over_that_promotions_students));

        // The seeded promotion's own roster is emptied: nothing of its own stands in the way.
        var seeded = world.Db.Registrations.Single();
        seeded.AcademicGroupId = null;

        // A second promotion in the same year, whose roster still holds a student.
        var otherLevel = new Level
        {
            Id = 77, Label = "4ème année", Year = 4, AcademicProgram = AcademicProgram.Medecine,
        };
        world.Db.Levels.Add(otherLevel);
        var otherRoster = world.Db.SeedGroup(501, 1, levelId: otherLevel.Id);
        world.Db.SeedRegistration("Nabil", "Cherkaoui", otherRoster, levelId: otherLevel.Id);
        world.Db.SaveChanges();

        var handler = new DeleteAllGroupsCommandHandler(world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail());

        // Year-wide the inhabited roster is in scope, so the refusal is correct.
        var yearWide = await handler.Handle(
            new DeleteAllGroupsCommand(TestHarness.CurrentYearId), default);
        yearWide.IsFailure.Should().BeTrue();
        yearWide.Error.Code.Should().Be("AcademicGroups.HasStudents");
        yearWide.Error.Description.Should().Contain("1 étudiant",
            "a refusal that does not say how many leaves the operator nothing to act on");

        // Scoped to the promotion that still holds one, it names that promotion.
        var inhabited = await handler.Handle(
            new DeleteAllGroupsCommand(TestHarness.CurrentYearId, otherLevel.Id), default);
        inhabited.IsFailure.Should().BeTrue();
        inhabited.Error.Code.Should().Be("AcademicGroups.HasStudents");
        inhabited.Error.Description.Should().Contain("4ème année");

        // ⚠ And scoped to the emptied promotion it is NOT refused — the whole point.
        //
        // Asserted as « it reached the write », for the reason the emptying twin above is: the delete
        // is ExecuteDelete, which the in-memory provider refuses outright, so getting that far IS the
        // observable fact that the guard let the act through. Written as an IsSuccess assertion the
        // case would be unreachable and the scoping would go untested.
        var clean = async () => await handler.Handle(
            new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        (await clean.Should().ThrowAsync<InvalidOperationException>(
                "the promotion's rosters hold nobody, so the guard passes and the delete is attempted"))
            .WithMessage("*ExecuteDelete*");
    }

    [Fact]
    public async Task Deleting_a_promotions_rosters_is_refused_while_its_own_rotations_are_underway()
    {
        // The other guard, at the new scope. It must name the promotion rather than the year, or it
        // reports a cost belonging to promotions nobody is touching.
        var world = Seed(nameof(Deleting_a_promotions_rosters_is_refused_while_its_own_rotations_are_underway),
            started: true);

        world.Db.Registrations.Single().AcademicGroupId = null;
        world.Db.SaveChanges();

        var result = await new DeleteAllGroupsCommandHandler(world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail())
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.PromotionRostersUnderway");
        result.Error.Description.Should().Contain("3ème année");

        world.Db.Cohorts.Should().ContainSingle("a refused delete must not have destroyed anything");
        world.Db.ServicePeriods.Should().ContainSingle();
    }

    [Fact]
    public async Task Deleting_an_unknown_promotion_is_refused_rather_than_widened_to_the_year()
    {
        // Same rule as its emptying twin, and it matters more here: widening on absence would delete
        // every promotion's rosters after a check that only looked at one.
        var world = Seed(nameof(Deleting_an_unknown_promotion_is_refused_rather_than_widened_to_the_year));

        var result = await new DeleteAllGroupsCommandHandler(world.Db, new AffectationTollReader(world.Db), new RecordingAuditTrail())
            .Handle(new DeleteAllGroupsCommand(TestHarness.CurrentYearId, 4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
        world.Db.AcademicGroups.Should().NotBeEmpty("nothing may be deleted on the way to failing");
    }

    [Fact]
    public void The_two_bulk_roster_acts_are_told_apart_in_the_audit_register()
    {
        // A register that calls both scopes « YEAR_GROUPS_DELETED » cannot answer which act was run —
        // which is the gap that made « qu'ai-je réellement fait ? » unanswerable on 2026-09-07.
        new DeleteAllGroupsCommand(TestHarness.CurrentYearId).AuditAction
            .Should().Be("YEAR_GROUPS_DELETED");

        var promotion = new DeleteAllGroupsCommand(TestHarness.CurrentYearId, TestHarness.LevelId);
        promotion.AuditAction.Should().Be("PROMOTION_GROUPS_DELETED");
        promotion.AuditMetadata.Should().Contain(TestHarness.LevelId.ToString(),
            "the year is already the entry's EntityId; the promotion is the whole difference");
    }

    // --- « Supprimer la cohorte » / « Réinitialiser les cohortes » ------------

    [Fact]
    public async Task Deleting_a_cohort_whose_rotation_started_is_refused_and_keeps_the_periods()
    {
        var world = Seed(
            nameof(Deleting_a_cohort_whose_rotation_started_is_refused_and_keeps_the_periods),
            started: true);

        var result = await DeleteHandler(world.Db).Handle(new DeleteCohortCommand(world.Cohort.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cohorts.AffectationsUnderway");

        world.Db.Cohorts.Should().ContainSingle();
        world.Db.ServicePeriods.Should().ContainSingle();
        world.Db.InternshipAssignments.Should().ContainSingle();
    }

    [Fact]
    public async Task Deleting_a_cohort_that_is_only_planned_succeeds_and_names_what_it_took()
    {
        var world = Seed(nameof(Deleting_a_cohort_that_is_only_planned_succeeds_and_names_what_it_took));

        var result = await DeleteHandler(world.Db).Handle(new DeleteCohortCommand(world.Cohort.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AffectationsRemoved.Should().Be(1);
        result.Value.PeriodsRemoved.Should().Be(1);

        world.Db.Cohorts.Should().BeEmpty();
        world.Db.InternshipAssignments.Should().BeEmpty();
        world.Db.ServicePeriods.Should().BeEmpty();
        // The roster survives its cohorte, and so does the registration's pointer at it.
        world.Db.AcademicGroups.Should().ContainSingle();
        world.Db.Registrations.Single().AcademicGroupId.Should().Be(world.Cohort.AcademicGroupId);
    }

    [Fact]
    public async Task An_evaluated_cohort_is_refused_even_though_nothing_is_running()
    {
        // Engaged is read from the assignment's status as well as from the périodes: a verdict stands
        // over a rotation that is finished rather than underway, and is still nobody's to delete
        // sideways.
        var db = TestHarness.NewContext(nameof(An_evaluated_cohort_is_refused_even_though_nothing_is_running));
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Nadia", "Chraibi", cohort.AcademicGroup);
        db.SeedGradedAssignment(registration, cohort, service, mark: 14m);
        db.SaveChanges();

        var result = await DeleteHandler(db).Handle(new DeleteCohortCommand(cohort.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cohorts.AffectationsUnderway");
        result.Error.Description.Should().Contain("1 évaluation");
        db.ServiceEvaluation.Should().ContainSingle();
    }

    [Fact]
    public async Task Resetting_a_stages_cohorts_is_refused_while_one_is_underway_and_names_the_stage()
    {
        var world = Seed(
            nameof(Resetting_a_stages_cohorts_is_refused_while_one_is_underway_and_names_the_stage),
            started: true);

        var result = await ResetHandler(world.Db)
            .Handle(new DeleteAllCohortsCommand(TestHarness.StageId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cohorts.StageUnderway");
        // « des affectations sont déjà en cours » named nothing an admin could act on.
        result.Error.Description.Should().Contain("Cardiologie").And.Contain("1 démarrée");
        world.Db.Cohorts.Should().ContainSingle();
    }

    [Fact]
    public async Task Resetting_a_stage_that_never_ran_this_year_reaches_no_other_year()
    {
        // ⚠ The year used to be optional and unresolved, so a null meant "every year this stage ever
        // ran" — on the one command in this area that deletes rows. CHIRURGIE holds 563 cohortes
        // across six years.
        var db = TestHarness.NewContext(nameof(Resetting_a_stage_that_never_ran_this_year_reaches_no_other_year));
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(
            TestHarness.PreviousYearId, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var service = db.SeedService(ServiceId, "Cardiologie");
        var past = db.SeedCohort(stage, 20, "Groupe 20", academicYearId: TestHarness.PreviousYearId);
        var registration = db.SeedRegistration(
            "Hicham", "Tazi", past.AcademicGroup, academicYearId: TestHarness.PreviousYearId);
        var assignment = db.SeedAssignment(registration, past);
        db.SeedPeriod(assignment, service, Start, End, started: false);
        db.SaveChanges();

        var result = await ResetHandler(db).Handle(new DeleteAllCohortsCommand(TestHarness.StageId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CohortsRemoved.Should().Be(0);
        db.Cohorts.Should().ContainSingle();
        db.InternshipAssignments.Should().ContainSingle();
    }
}
