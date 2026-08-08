using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cnpn.Manage;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Recording and correcting the ministerial texts. Until these commands existed a new arrêté could
/// only be added in SQL, which made the CNPN feature unusable without a developer.
///
/// The guards all protect the same thing: a text is not free-form metadata. Its span decides which
/// levels can carry requirements, and its intake year decides which promotion it claims — so neither
/// can be edited into a state the rest of the model cannot honour.
/// </summary>
public class CnpnVersionManagementTests
{
    private const int Year2024 = 12;

    private static void SeedYears(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedAcademicYear(Year2024, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
    }

    private static CreateCnpnVersionCommand New(
        string code = "9999.99",
        AcademicProgram program = AcademicProgram.Medecine,
        int totalYears = 6,
        int? intakeYearId = Year2024) =>
        new(code, $"CNPN {code}", program, totalYears, "BO test", intakeYearId);

    private static CreateCnpnVersionCommandHandler Creator(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    private static UpdateCnpnVersionCommandHandler Updater(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    // ── Recording a text ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_newly_published_arrete_can_be_recorded()
    {
        await using var db = TestHarness.NewContext("cnpn-create");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await Creator(db).Handle(New(), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.CnpnVersions.SingleAsync(v => v.Code == "9999.99");
        saved.TotalYears.Should().Be(6);
        saved.AppliesToEntrantsFromAcademicYearId.Should().Be(Year2024);
    }

    [Fact]
    public async Task A_text_kept_only_for_citation_needs_no_intake_year()
    {
        await using var db = TestHarness.NewContext("cnpn-create-no-intake");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await Creator(db).Handle(New(intakeYearId: null), default);

        result.IsSuccess.Should().BeTrue();
        (await db.CnpnVersions.SingleAsync(v => v.Code == "9999.99"))
            .AppliesToEntrantsFromAcademicYearId.Should().BeNull();
    }

    [Fact]
    public async Task Two_texts_of_one_programme_cannot_share_a_reference()
    {
        await using var db = TestHarness.NewContext("cnpn-dup-code");
        SeedYears(db);
        await db.SaveChangesAsync();
        await Creator(db).Handle(New(code: "1650.25"), default);

        var result = await Creator(db).Handle(New(code: "1650.25", intakeYearId: null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.DuplicateCode");
    }

    [Fact]
    public async Task The_same_reference_may_exist_in_another_programme()
    {
        await using var db = TestHarness.NewContext("cnpn-code-cross-programme");
        SeedYears(db);
        await db.SaveChangesAsync();
        await Creator(db).Handle(New(code: "1650.25"), default);

        var result = await Creator(db).Handle(
            New(code: "1650.25", program: AcademicProgram.Pharmacie), default);

        result.IsSuccess.Should().BeTrue("codes are unique per filière, not globally");
    }

    [Fact]
    public async Task Two_texts_cannot_claim_the_same_promotion()
    {
        // Version selection resolves "the latest intake at or before entry"; a tie has no winner.
        await using var db = TestHarness.NewContext("cnpn-intake-clash");
        SeedYears(db);
        await db.SaveChangesAsync();
        await Creator(db).Handle(New(code: "A"), default);

        var result = await Creator(db).Handle(New(code: "B"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.IntakeYearAlreadyTaken");
        result.Error.Description.Should().Contain("A");
    }

    [Fact]
    public async Task An_unknown_intake_year_is_refused()
    {
        await using var db = TestHarness.NewContext("cnpn-intake-unknown");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await Creator(db).Handle(New(intakeYearId: 999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicYears.NotFound");
    }

    [Fact]
    public async Task Only_administration_may_record_a_text()
    {
        await using var db = TestHarness.NewContext("cnpn-create-authz");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await new CreateCnpnVersionCommandHandler(db, db.StrangerAuthorizer())
            .Handle(New(), default);

        result.IsFailure.Should().BeTrue();
        (await db.CnpnVersions.CountAsync(v => v.Code == "9999.99")).Should().Be(0);
    }

    // ── Correcting a text ────────────────────────────────────────────────────

    [Fact]
    public async Task A_placeholder_can_be_renamed()
    {
        // Exactly the PHARM-LEGACY case: a row created so the data had somewhere to go.
        await using var db = TestHarness.NewContext("cnpn-rename");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(code: "PHARM-LEGACY"), default)).Value;

        var result = await Updater(db).Handle(
            new UpdateCnpnVersionCommand(id, "2175.19", "CNPN Pharmacie 2019", 6, "BO réel", Year2024),
            default);

        result.IsSuccess.Should().BeTrue();
        (await db.CnpnVersions.SingleAsync(v => v.Id == id)).Code.Should().Be("2175.19");
    }

    [Fact]
    public async Task A_degree_cannot_be_shortened_below_a_level_that_already_has_requirements()
    {
        await using var db = TestHarness.NewContext("cnpn-shorten");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(totalYears: 7), default)).Value;

        // SeedCatalog's shared level is year 3; record something against it.
        db.Curriculums.Add(new Curriculum { LevelId = TestHarness.LevelId, CnpnVersionId = id });
        await db.SaveChangesAsync();

        var result = await Updater(db).Handle(
            new UpdateCnpnVersionCommand(id, "9999.99", "CNPN", 2, null, Year2024), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CannotShortenBelowRecordedLevel");
        result.Error.Description.Should().Contain("3");
    }

    [Fact]
    public async Task Shortening_is_allowed_when_nothing_is_stranded()
    {
        await using var db = TestHarness.NewContext("cnpn-shorten-ok");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(totalYears: 7), default)).Value;

        var result = await Updater(db).Handle(
            new UpdateCnpnVersionCommand(id, "9999.99", "CNPN", 6, null, Year2024), default);

        result.IsSuccess.Should().BeTrue();
        (await db.CnpnVersions.SingleAsync(v => v.Id == id)).TotalYears.Should().Be(6);
    }

    [Fact]
    public async Task An_unknown_text_cannot_be_corrected()
    {
        await using var db = TestHarness.NewContext("cnpn-update-missing");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await Updater(db).Handle(
            new UpdateCnpnVersionCommand(999, "X", "X", 6, null, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnVersions.NotFound");
    }

    // ── Deleting: for the mistyped row, never for a text anyone follows ──────

    [Fact]
    public async Task A_text_nobody_follows_can_be_removed()
    {
        await using var db = TestHarness.NewContext("cnpn-delete");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(code: "TYPO"), default)).Value;

        var result = await new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0, "it carried no requirements");
        (await db.CnpnVersions.CountAsync(v => v.Id == id)).Should().Be(0);
    }

    [Fact]
    public async Task A_text_a_student_follows_is_refused()
    {
        // The hard gate. Without it the Users → CnpnVersions foreign key (NO ACTION) throws, and the
        // student would be left following no CNPN at all.
        await using var db = TestHarness.NewContext("cnpn-delete-students");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(code: "EN-USAGE"), default)).Value;

        var reg = db.SeedRegistration("Suit", "CeTexte");
        reg.Student.AssignCnpnVersion(id, isInferred: false);
        await db.SaveChangesAsync();

        var result = await new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CannotDeleteWithStudents");
        result.Error.Description.Should().Contain("EN-USAGE");
        (await db.CnpnVersions.CountAsync(v => v.Id == id)).Should().Be(1);
    }

    [Fact]
    public async Task An_inferred_stamp_still_counts_as_a_student()
    {
        // A deduced assignment is still someone following the text; deleting it under them would
        // turn an uncertain answer into no answer.
        await using var db = TestHarness.NewContext("cnpn-delete-inferred");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(code: "DEDUIT"), default)).Value;

        var reg = db.SeedRegistration("Déduit", "Étudiant");
        reg.Student.AssignCnpnVersion(id, isInferred: true);
        await db.SaveChangesAsync();

        var result = await new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CannotDeleteWithStudents");
    }

    [Fact]
    public async Task Removing_a_text_reports_the_requirements_that_went_with_it()
    {
        // Destructive and deliberately allowed: a text nobody follows has nobody who could owe
        // anything, so its requirement sets strand no obligation. The count is returned so the
        // confirmation can say it out loud.
        //
        // ⚠ This asserts the *count*, not the cascade — UseInMemoryDatabase ignores OnDelete, so
        // that Curriculums actually disappear is only verifiable against PostgreSQL. The FK is
        // ON DELETE CASCADE in the schema; see SCHEMA.md.
        await using var db = TestHarness.NewContext("cnpn-delete-cascade");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(code: "AVEC-EXIGENCES", totalYears: 6), default)).Value;
        SeedSourceText(db, id, 1, 2, 3);
        await db.SaveChangesAsync();

        var result = await new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3, "the caller must be told what the cascade took");
    }

    [Fact]
    public async Task An_unknown_text_cannot_be_removed()
    {
        await using var db = TestHarness.NewContext("cnpn-delete-missing");
        SeedYears(db);
        await db.SaveChangesAsync();

        var result = await new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(999), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnVersions.NotFound");
    }

    [Fact]
    public async Task Only_administration_may_remove_a_text()
    {
        await using var db = TestHarness.NewContext("cnpn-delete-authz");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(), default)).Value;

        var result = await new DeleteCnpnVersionCommandHandler(db, db.StrangerAuthorizer())
            .Handle(new DeleteCnpnVersionCommand(id), default);

        result.IsFailure.Should().BeTrue();
        (await db.CnpnVersions.CountAsync(v => v.Id == id)).Should().Be(1);
    }

    // ── « 1650.25 reprend 2174.18 » ──────────────────────────────────────────

    private static void SeedSourceText(ApplicationDbContext db, int versionId, params int[] levelYears)
    {
        foreach (int year in levelYears)
        {
            int levelId = 200 + year;
            if (db.Levels.Local.All(l => l.Id != levelId))
                db.Levels.Add(new Level
                {
                    Id = levelId, Label = $"{year}e", Year = year,
                    AcademicProgram = AcademicProgram.Medecine,
                });

            int stageId = 300 + year;
            db.Stages.Add(new Stage
            {
                Id = stageId, Name = $"Stage {year}", LevelId = levelId,
                Coefficient = 2, DurationInDays = 30,
            });

            var set = new Curriculum { LevelId = levelId, CnpnVersionId = versionId };
            set.AddStage(stageId, 2, 30);
            db.Curriculums.Add(set);
        }
    }

    [Fact]
    public async Task Cloning_a_whole_text_seeds_every_level_at_once()
    {
        await using var db = TestHarness.NewContext("cnpn-clone-all");
        SeedYears(db);
        await db.SaveChangesAsync();

        int from = (await Creator(db).Handle(New(code: "OLD", totalYears: 7, intakeYearId: null), default)).Value;
        int to   = (await Creator(db).Handle(New(code: "NEW", totalYears: 6), default)).Value;
        SeedSourceText(db, from, 1, 2, 3, 4, 5, 6);
        await db.SaveChangesAsync();

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(from, to), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.LevelsCloned.Should().Be(6, "one action instead of six");
        result.Value.StagesCopied.Should().Be(6);
        (await db.Curriculums.CountAsync(c => c.CnpnVersionId == to)).Should().Be(6);
    }

    [Fact]
    public async Task A_level_beyond_the_targets_span_is_skipped_and_counted()
    {
        // The 7e année of a seven-year text has nowhere to go in a six-year one.
        await using var db = TestHarness.NewContext("cnpn-clone-outside");
        SeedYears(db);
        await db.SaveChangesAsync();

        int from = (await Creator(db).Handle(New(code: "OLD", totalYears: 7, intakeYearId: null), default)).Value;
        int to   = (await Creator(db).Handle(New(code: "NEW", totalYears: 6), default)).Value;
        SeedSourceText(db, from, 6, 7);
        await db.SaveChangesAsync();

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(from, to), default);

        result.Value.LevelsCloned.Should().Be(1);
        result.Value.LevelsOutsideProgramme.Should().Be(1);
    }

    [Fact]
    public async Task A_level_already_recorded_by_hand_is_never_overwritten()
    {
        await using var db = TestHarness.NewContext("cnpn-clone-skip");
        SeedYears(db);
        await db.SaveChangesAsync();

        int from = (await Creator(db).Handle(New(code: "OLD", totalYears: 6, intakeYearId: null), default)).Value;
        int to   = (await Creator(db).Handle(New(code: "NEW", totalYears: 6), default)).Value;
        SeedSourceText(db, from, 1, 2);
        db.Curriculums.Add(new Curriculum { LevelId = 201, CnpnVersionId = to, Reference = "saisi à la main" });
        await db.SaveChangesAsync();

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(from, to), default);

        result.Value.LevelsCloned.Should().Be(1);
        result.Value.LevelsSkipped.Should().Be(1);
        (await db.Curriculums.SingleAsync(c => c.CnpnVersionId == to && c.LevelId == 201))
            .Reference.Should().Be("saisi à la main");
    }

    [Fact]
    public async Task Cloning_across_programmes_is_refused()
    {
        await using var db = TestHarness.NewContext("cnpn-clone-programme");
        SeedYears(db);
        await db.SaveChangesAsync();

        int from = (await Creator(db).Handle(New(code: "MED", intakeYearId: null), default)).Value;
        int to   = (await Creator(db).Handle(
            New(code: "PHARM", program: AcademicProgram.Pharmacie), default)).Value;
        SeedSourceText(db, from, 1);
        await db.SaveChangesAsync();

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(from, to), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CloneProgramMismatch");
    }

    [Fact]
    public async Task Cloning_a_text_onto_itself_is_refused()
    {
        await using var db = TestHarness.NewContext("cnpn-clone-self");
        SeedYears(db);
        await db.SaveChangesAsync();
        int id = (await Creator(db).Handle(New(), default)).Value;

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(id, id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CloneIntoItself");
    }

    [Fact]
    public async Task Cloning_from_an_empty_text_says_so()
    {
        await using var db = TestHarness.NewContext("cnpn-clone-empty");
        SeedYears(db);
        await db.SaveChangesAsync();

        int from = (await Creator(db).Handle(New(code: "OLD", intakeYearId: null), default)).Value;
        int to   = (await Creator(db).Handle(New(code: "NEW"), default)).Value;
        await db.SaveChangesAsync();

        var result = await new CloneCnpnCurriculaCommandHandler(db, db.AdminAuthorizer())
            .Handle(new CloneCnpnCurriculaCommand(from, to), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CloneSourceEmpty");
    }
}
