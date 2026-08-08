using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Curricula.Copy;
using PGSH.Application.Stages.Curricula.Save;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Recording a newly published CNPN. The whole set is submitted at once because that is how a text is
// issued; reconciling against what is stored is what makes a dropped stage announce itself instead of
// disappearing silently.
public class CurriculumEditingTests
{
    private const int Clinique1 = TestHarness.StageId;
    private const int Clinique2 = 82;
    private const int Clinique3 = 83;
    private const int OtherLevelId = 9;

    private static void SeedCatalogue(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        stage.Name = "Pharmacie Clinique 1";
        db.SeedStage(Clinique2, "Pharmacie Clinique 2", coefficient: 2);
        db.SeedStage(Clinique3, "Pharmacie Clinique 3", coefficient: 2);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2022-2023",
            new DateOnly(2022, 9, 1), new DateOnly(2023, 8, 31));
    }

    private static SaveCurriculumCommand Save(int cnpnVersionId, params (int Id, int Coef, int Days)[] stages) =>
        new(TestHarness.LevelId, cnpnVersionId, "Arrêté ministériel",
            [.. stages.Select(s => new CurriculumStageInput(s.Id, s.Coef, s.Days))]);

    private static SaveCurriculumCommandHandler Saver(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    // ── The text has to fit the level it is recorded against ─────────────────

    [Fact]
    public async Task A_level_beyond_the_programmes_span_cannot_be_given_requirements()
    {
        // The whole point of recording TotalYears: a six-year CNPN has no seventh year, so requiring
        // stages of one would create an obligation nobody can ever serve.
        await using var db = TestHarness.NewContext("cnpn-level-outside");
        SeedCatalogue(db);

        const int SeventhYear = 77;
        db.Levels.Add(new Level
        {
            Id = SeventhYear, Label = "7ème année", Year = 7,
            AcademicProgram = AcademicProgram.Medecine,
        });
        db.Stages.Add(new Stage
        {
            Id = 78, Name = "Internat", LevelId = SeventhYear, Coefficient = 1, DurationInDays = 30,
        });
        await db.SaveChangesAsync();

        var result = await Saver(db).Handle(
            new SaveCurriculumCommand(SeventhYear, TestHarness.NewCnpnId, null,
                [new CurriculumStageInput(78, 1, 30)]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Curriculums.LevelOutsideProgramme");
        result.Error.Description.Should().Contain("6 années");
    }

    [Fact]
    public async Task An_unknown_text_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cnpn-version-missing");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var result = await Saver(db).Handle(Save(999, (Clinique1, 2, 42)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnVersions.NotFound");
    }

    [Fact]
    public async Task A_level_of_another_programme_cannot_be_recorded_against_this_text()
    {
        await using var db = TestHarness.NewContext("cnpn-program-mismatch");
        SeedCatalogue(db);
        db.SeedCnpnVersion(95, "PHARM", totalYears: 6, program: AcademicProgram.Pharmacie);
        await db.SaveChangesAsync();

        // The shared level is Médecine; the text is Pharmacie.
        var result = await Saver(db).Handle(Save(95, (Clinique1, 2, 42)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Curriculums.ProgramMismatch");
    }

    [Fact]
    public async Task A_newly_published_text_is_recorded()
    {
        await using var db = TestHarness.NewContext("cnpn-save-new");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var result = await Saver(db).Handle(
            Save(TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42)), default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.Curriculums.Include(c => c.Stages).SingleAsync();
        stored.Stages.Should().HaveCount(2);
        stored.Reference.Should().Be("Arrêté ministériel");
    }

    [Fact]
    public async Task Saving_the_same_set_twice_leaves_the_same_text()
    {
        // The endpoint is a PUT; sending the set again must not duplicate anything.
        await using var db = TestHarness.NewContext("cnpn-save-idempotent");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var handler = Saver(db);
        var command = Save(TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42));

        var first = await handler.Handle(command, default);
        var second = await handler.Handle(command, default);

        second.Value.Should().Be(first.Value);
        db.Curriculums.Should().ContainSingle();
        (await db.Curriculums.Include(c => c.Stages).SingleAsync()).Stages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dropping_a_stage_from_the_text_announces_it()
    {
        // Removal has to be visible: students who failed that stage still owe it.
        await using var db = TestHarness.NewContext("cnpn-save-drop");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var handler = Saver(db);
        await handler.Handle(
            Save(TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42), (Clinique3, 2, 42)),
            default);

        var curriculum = await db.Curriculums.Include(c => c.Stages).SingleAsync();
        curriculum.ClearDomainEvents();

        await handler.Handle(
            Save(TestHarness.NewCnpnId, (Clinique1, 2, 42), (Clinique2, 2, 42)), default);

        curriculum.Stages.Should().HaveCount(2);
        curriculum.Stages.Should().NotContain(s => s.StageId == Clinique3);
        curriculum.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CurriculumStageRemovedDomainEvent>()
            .Which.StageId.Should().Be(Clinique3);
    }

    [Fact]
    public async Task Keeping_a_stage_but_reweighting_it_is_an_amendment_not_a_removal()
    {
        await using var db = TestHarness.NewContext("cnpn-save-reweight");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var handler = Saver(db);
        await handler.Handle(Save(TestHarness.NewCnpnId, (Clinique1, 2, 42)), default);

        var curriculum = await db.Curriculums.Include(c => c.Stages).SingleAsync();
        curriculum.ClearDomainEvents();

        await handler.Handle(Save(TestHarness.NewCnpnId, (Clinique1, 3, 66)), default);

        var entry = curriculum.Stages.Should().ContainSingle().Subject;
        entry.Coefficient.Should().Be(3);
        entry.DurationInDays.Should().Be(66);
        curriculum.DomainEvents.Should().BeEmpty("the stage was kept, only reweighted");
    }

    [Fact]
    public async Task A_stage_from_another_level_cannot_be_required()
    {
        await using var db = TestHarness.NewContext("cnpn-save-foreign");
        SeedCatalogue(db);

        var otherLevel = new Level { Id = OtherLevelId, Label = "6ème année", Year = 6 };
        db.Levels.Add(otherLevel);
        db.Stages.Add(new Stage
        {
            Id = 42, Name = "Chirurgie", LevelId = OtherLevelId, Level = otherLevel, Coefficient = 1,
        });
        await db.SaveChangesAsync();

        var result = await Saver(db).Handle(Save(TestHarness.NewCnpnId, (42, 1, 30)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CurriculumErrors.StageNotInLevel(42, TestHarness.LevelId));
    }

    [Fact]
    public async Task An_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cnpn-save-unknown-stage");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var result = await Saver(db).Handle(Save(TestHarness.NewCnpnId, (999, 1, 30)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.NotFound");
    }

    [Fact]
    public async Task Only_the_administration_may_record_a_text()
    {
        await using var db = TestHarness.NewContext("cnpn-save-forbidden");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var handler = new SaveCurriculumCommandHandler(db, db.StrangerAuthorizer());
        var result = await handler.Handle(Save(TestHarness.NewCnpnId, (Clinique1, 2, 42)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AdministrativeOnly);
    }

    [Fact]
    public async Task A_year_is_opened_by_cloning_the_previous_text()
    {
        await using var db = TestHarness.NewContext("cnpn-copy");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        await Saver(db).Handle(
            Save(TestHarness.OldCnpnId, (Clinique1, 2, 42), (Clinique2, 3, 66)), default);

        var result = await new CopyCurriculumCommandHandler(db, db.AdminAuthorizer()).Handle(
            new CopyCurriculumCommand(TestHarness.LevelId, TestHarness.OldCnpnId, TestHarness.NewCnpnId),
            default);

        result.IsSuccess.Should().BeTrue();

        var copy = await db.Curriculums
            .Include(c => c.Stages)
            .SingleAsync(c => c.CnpnVersionId == TestHarness.NewCnpnId);

        copy.Stages.Should().HaveCount(2);
        // Each year is an independent record — the weights come across, not a pointer to last year.
        copy.Stages.Single(s => s.StageId == Clinique2).Coefficient.Should().Be(3);
        copy.Stages.Single(s => s.StageId == Clinique2).DurationInDays.Should().Be(66);
    }

    [Fact]
    public async Task Cloning_onto_a_year_that_already_has_a_text_is_refused()
    {
        // Copying opens a year; amending an existing one goes through Save, where each dropped stage
        // is announced instead of being replaced wholesale.
        await using var db = TestHarness.NewContext("cnpn-copy-exists");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var saver = Saver(db);
        await saver.Handle(Save(TestHarness.OldCnpnId, (Clinique1, 2, 42)), default);
        await saver.Handle(Save(TestHarness.NewCnpnId, (Clinique2, 2, 42)), default);

        var result = await new CopyCurriculumCommandHandler(db, db.AdminAuthorizer()).Handle(
            new CopyCurriculumCommand(TestHarness.LevelId, TestHarness.OldCnpnId, TestHarness.NewCnpnId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            CurriculumErrors.AlreadyExists(TestHarness.LevelId, TestHarness.NewCnpnId));
    }

    [Fact]
    public async Task Cloning_from_a_year_with_no_text_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("cnpn-copy-missing");
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var result = await new CopyCurriculumCommandHandler(db, db.AdminAuthorizer()).Handle(
            new CopyCurriculumCommand(TestHarness.LevelId, TestHarness.OldCnpnId, TestHarness.NewCnpnId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            CurriculumErrors.NotFound(TestHarness.LevelId, TestHarness.OldCnpnId));
    }
}
