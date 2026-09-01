using FluentAssertions;
using PGSH.Application.Stages.Curricula.Compare;
using PGSH.Application.Stages.Curricula.GetCurriculum;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Curricula.SeedFromHistory;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The read behind manual revalidation: a student is judged against the text of the year they failed
// in, but can only be re-planned against today's. Both have to be visible before anyone decides.
public class CurriculumComparisonTests
{
    private const int PharmacieY5 = TestHarness.LevelId;
    private const int Clinique1 = 81, Clinique2 = 82, Clinique3 = 83, Officine = 93;

    private static Curriculum Seed(
        ApplicationDbContext db, int id, int cnpnVersionId, params (int StageId, int Coef, int Days)[] stages)
    {
        var curriculum = new Curriculum { Id = id, LevelId = PharmacieY5, CnpnVersionId = cnpnVersionId };
        foreach (var (stageId, coef, days) in stages) curriculum.AddStage(stageId, coef, days);
        db.Curriculums.Add(curriculum);
        return curriculum;
    }

    private static void SeedStages(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        stage.Id = Clinique1; stage.Name = "Pharmacie Clinique 1";
        db.SeedStage(Clinique2, "Pharmacie Clinique 2", coefficient: 2);
        db.SeedStage(Clinique3, "Pharmacie Clinique 3", coefficient: 2);
        db.SeedStage(Officine, "Stage d'initiation en officine");
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2022-2023",
            new DateOnly(2022, 9, 1), new DateOnly(2023, 8, 31));
    }

    [Fact]
    public async Task A_stage_dropped_since_the_students_year_is_reported_as_removed()
    {
        // The real case: Pharmacie Clinique 3 ran 2019-20 → 2022-23 and then vanished, while
        // Clinique 1 and 2 carried on.
        await using var db = TestHarness.NewContext("cnpn-removed");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42), (Clinique3, 2, 42));
        Seed(db, 2, TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasChanges.Should().BeTrue();

        var removed = result.Value.Entries.Should().ContainSingle(e => e.Change == CurriculumChange.Removed).Subject;
        removed.StageId.Should().Be(Clinique3);
        removed.StageName.Should().Be("Pharmacie Clinique 3");
        removed.FromCoefficient.Should().Be(2);
        removed.ToCoefficient.Should().BeNull();     // nothing to serve it against today
    }

    [Fact]
    public async Task A_newly_required_stage_is_reported_as_added()
    {
        await using var db = TestHarness.NewContext("cnpn-added");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42));
        Seed(db, 2, TestHarness.NewCnpnId, (Clinique1, 2, 42), (Officine, 1, 30));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        result.Value.Entries.Should().ContainSingle(e => e.Change == CurriculumChange.Added)
            .Which.StageId.Should().Be(Officine);
    }

    [Fact]
    public async Task A_stage_kept_but_reweighted_is_not_reported_as_unchanged()
    {
        // A text can keep a stage and change what it is worth; that still matters to a transcript.
        await using var db = TestHarness.NewContext("cnpn-reweighted");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42));
        Seed(db, 2, TestHarness.NewCnpnId, (Clinique1, 3, 66));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        var entry = result.Value.Entries.Should().ContainSingle().Subject;
        entry.Change.Should().Be(CurriculumChange.Reweighted);
        entry.FromCoefficient.Should().Be(2);
        entry.ToCoefficient.Should().Be(3);
        entry.FromDurationInDays.Should().Be(42);
        entry.ToDurationInDays.Should().Be(66);
    }

    [Fact]
    public async Task Two_identical_years_report_no_changes()
    {
        await using var db = TestHarness.NewContext("cnpn-same");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42));
        Seed(db, 2, TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        result.Value.HasChanges.Should().BeFalse();
        result.Value.Entries.Should().OnlyContain(e => e.Change == CurriculumChange.Unchanged);
    }

    [Fact]
    public async Task Changes_are_listed_before_the_stages_that_did_not_move()
    {
        await using var db = TestHarness.NewContext("cnpn-order");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42), (Clinique3, 2, 42));
        Seed(db, 2, TestHarness.NewCnpnId, (Clinique1, 2, 42), (Officine, 1, 30));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        result.Value.Entries.Last().Change.Should().Be(CurriculumChange.Unchanged);
        result.Value.Entries.First().Change.Should().NotBe(CurriculumChange.Unchanged);
    }

    [Fact]
    public async Task A_year_with_no_recorded_curriculum_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cnpn-missing");
        SeedStages(db);
        Seed(db, 1, TestHarness.NewCnpnId, (Clinique1, 2, 42));
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.NotFound(PharmacieY5, TestHarness.OldCnpnId));
    }

    [Fact]
    public async Task The_curriculum_of_a_given_year_can_be_read_back()
    {
        await using var db = TestHarness.NewContext("cnpn-read");
        SeedStages(db);
        Seed(db, 1, TestHarness.OldCnpnId, (Clinique1, 2, 42), (Clinique3, 2, 42));
        await db.SaveChangesAsync();

        var result = await new GetCurriculumQueryHandler(db).Handle(
            new GetCurriculumQuery(PharmacieY5, TestHarness.OldCnpnId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Stages.Should().HaveCount(2);
        result.Value.CnpnVersionCode.Should().Be("2174.18");
    }

    [Fact]
    public async Task History_can_be_reconstituted_from_the_stages_actually_served()
    {
        // Before this feature nothing recorded the requirement set, so execution is the only evidence.
        await using var db = TestHarness.NewContext("cnpn-seed");
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, 10, "Groupe 10");
        await db.SaveChangesAsync();

        var handler = new SeedCurriculaFromHistoryCommandHandler(new CurriculumHistoryReconstructor(db, new CnpnAssignment(db)), db.AdminAuthorizer());

        var dryRun = await handler.Handle(new SeedCurriculaFromHistoryCommand(DryRun: true), default);
        dryRun.Value.CurriculaCreated.Should().Be(1);
        db.Curriculums.Should().BeEmpty("a dry run writes nothing");

        var applied = await handler.Handle(new SeedCurriculaFromHistoryCommand(DryRun: false), default);
        applied.Value.CurriculaCreated.Should().Be(1);
        applied.Value.StageEntriesCreated.Should().Be(1);
        db.Curriculums.Should().ContainSingle();
    }

    [Fact]
    public async Task Reconstitution_never_overwrites_a_curriculum_already_recorded()
    {
        // Once a year has been confirmed by hand it must survive a re-run.
        await using var db = TestHarness.NewContext("cnpn-seed-skip");
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, 10, "Groupe 10");
        Seed(db, 1, TestHarness.NewCnpnId, (TestHarness.StageId, 5, 99));
        await db.SaveChangesAsync();

        var result = await new SeedCurriculaFromHistoryCommandHandler(new CurriculumHistoryReconstructor(db, new CnpnAssignment(db)), db.AdminAuthorizer())
            .Handle(new SeedCurriculaFromHistoryCommand(DryRun: false), default);

        result.Value.CurriculaCreated.Should().Be(0);
        result.Value.CurriculaSkippedBecauseTheyExist.Should().Be(1);
        db.Curriculums.Single().Stages.Single().Coefficient.Should().Be(5, "the hand-entered value stands");
    }

    [Fact]
    public async Task Only_the_administration_may_reconstitute_history()
    {
        await using var db = TestHarness.NewContext("cnpn-seed-forbidden");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await new SeedCurriculaFromHistoryCommandHandler(new CurriculumHistoryReconstructor(db, new CnpnAssignment(db)), db.StrangerAuthorizer())
            .Handle(new SeedCurriculaFromHistoryCommand(DryRun: false), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AdministrativeOnly);
    }
}
