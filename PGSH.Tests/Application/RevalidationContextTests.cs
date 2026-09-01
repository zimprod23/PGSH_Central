using FluentAssertions;
using PGSH.Application.Calendar;
using PGSH.Application.Stages.Revalidation;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The read behind the revalidation dialog, and the one rule it exists to enforce: the window it
/// proposes comes from <b>the registration's own text</b>, never from the catalogue.
///
/// <para>Measured on the live base 2026-09-01 — MED3 Chirurgie reads 30 jours ouvrables in the
/// catalogue since it was aligned on arrêté 1650.25, while the 92 students still owing it in 6ᵉ
/// année are governed by 2174.18, which states <b>66</b>; the one such window on record ran 65. So a
/// proposal taken from <c>Stage.DurationInDays</c> is wrong for precisely the population that
/// reaches this screen: a revalidation is by construction a student on an older text.</para>
/// </summary>
public class RevalidationContextTests
{
    private const int OldText = TestHarness.OldCnpnId;   // stands in for 2174.18
    private const int NewText = TestHarness.NewCnpnId;   // stands in for 1650.25
    private const int EarlierYear = 90;
    private const int FailureServiceId = 77;

    private static GetRevalidationContextQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new WorkingDayProvider(db), db.AdminAuthorizer());

    /// <summary>
    /// A student who failed the stage under the old text and is registered again a year later. The
    /// catalogue has since moved to the new text's figure — the live shape after the alignment.
    /// </summary>
    private static async Task<(ApplicationDbContext Db, Guid Current)> SeedAsync(
        string name, decimal earlierMark = 7m, bool currentHasRoster = true)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();

        stage.Coefficient = 3;
        stage.DurationInDays = 30;          // the catalogue, aligned on the new text

        var oldSet = new Curriculum { Id = 1, LevelId = TestHarness.LevelId, CnpnVersionId = OldText };
        oldSet.AddStage(stage.Id, 3, 66);   // what the student is actually governed by
        var newSet = new Curriculum { Id = 2, LevelId = TestHarness.LevelId, CnpnVersionId = NewText };
        newSet.AddStage(stage.Id, 1, 30);
        db.Curriculums.AddRange(oldSet, newSet);

        db.SeedAcademicYear(EarlierYear, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var group = db.SeedGroup(500, 1);
        var cohort = db.SeedCohortFor(stage, group, 600);
        var service = db.SeedService(FailureServiceId, "Chirurgie Vasculaire");

        // Driven through the real lifecycle rather than by assigning Result: a mark under 10 is what
        // makes an attempt NonValidé, and seeding the verdict directly would prove nothing about the
        // path the student actually took.
        var failedReg = db.SeedRegistration("Jad", "Abdallah", group, academicYearId: EarlierYear);
        db.SeedGradedAssignment(failedReg, cohort, service, earlierMark, new DateOnly(2025, 3, 18));

        // The retake hangs off the registration he holds NOW, which carries its own stamp — and it
        // must be the SAME student. SeedRegistration mints a fresh one per call, so a second call
        // here would make the prior attempt invisible to PriorAttemptsQuery and every case would
        // silently degrade to NothingToRevalidate.
        var currentReg = new Registration
        {
            Id = Guid.NewGuid(),
            AcademicYearId = TestHarness.CurrentYearId,
            LevelId = TestHarness.LevelId,
            StudentId = failedReg.StudentId,
            Student = failedReg.Student,
            AcademicGroupId = currentHasRoster ? group.Id : null,
        };
        currentReg.StampCnpnVersion(OldText, RegistrationCnpnSource.Backfilled);
        db.Registrations.Add(currentReg);

        await db.SaveChangesAsync();
        return (db, currentReg.Id);
    }

    [Fact]
    public async Task The_proposed_window_is_laid_from_the_texts_duration_not_the_catalogues()
    {
        var (db, current) = await SeedAsync("reval-ctx-text-duration");
        await using var _ = db;

        var result = await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, new DateOnly(2026, 10, 5)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ctx = result.Value;

        ctx.GoverningText!.DurationInDays.Should().Be(66);
        ctx.CatalogueDurationInDays.Should().Be(30);

        // The whole point: 66 worked days, the figure his text states — not the catalogue's 30.
        ctx.ProposedWindow!.WorkingDays.Should().Be(66);
        ctx.ProposedWindow.CalendarDays.Should().BeGreaterThan(66);
    }

    [Fact]
    public async Task The_catalogue_figure_is_reported_beside_it_never_instead_of_it()
    {
        var (db, current) = await SeedAsync("reval-ctx-catalogue-shown");
        await using var _ = db;

        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        // Both numbers travel, so the screen can show the disagreement rather than pick a winner.
        ctx.CatalogueCoefficient.Should().Be(3);
        ctx.GoverningText!.Coefficient.Should().Be(3);
        ctx.GoverningText.Code.Should().Be("2174.18");
        ctx.GoverningText.FromRegistration.Should().BeTrue();
    }

    [Fact]
    public async Task A_text_that_states_nothing_for_the_stage_proposes_no_window()
    {
        var (db, current) = await SeedAsync("reval-ctx-text-silent");
        await using var _ = db;

        // 1650.25's requirement sets are genuinely not fully entered, so a text saying nothing about
        // a stage is the ordinary case here, not an edge one.
        var set = db.Curriculums.Single(c => c.CnpnVersionId == OldText);
        set.Stages.Clear();
        await db.SaveChangesAsync();

        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        // Absence is not zero, and it is not the catalogue's 30 either: nothing is proposed, and the
        // response says why. An invented proposal is indistinguishable from an authored one.
        ctx.GoverningText!.StatesThisStage.Should().BeFalse();
        ctx.GoverningText.DurationInDays.Should().BeNull();
        ctx.ProposedWindow.Should().BeNull();
        ctx.CatalogueDurationInDays.Should().Be(30);
    }

    [Fact]
    public async Task The_last_failure_carries_the_service_and_what_was_actually_served()
    {
        var (db, current) = await SeedAsync("reval-ctx-last-failure");
        await using var _ = db;

        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        ctx.LastFailure!.ServiceName.Should().Be("Chirurgie Vasculaire");
        ctx.LastFailure.ServiceId.Should().Be(FailureServiceId);

        // The only figure on the screen that is neither a catalogue value nor a text value.
        ctx.LastFailure.WorkingDaysServed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_preview_refuses_on_exactly_the_rules_the_command_refuses_on()
    {
        // Passed the earlier attempt: the command answers StageAlreadyValidated, so the dialog
        // must not offer the act.
        var (db, current) = await SeedAsync("reval-ctx-refusal", earlierMark: 14m);
        await using var _ = db;

        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        ctx.CanOpen.Should().BeFalse();
        ctx.RefusalCode.Should().Be(StageErrors.StageAlreadyValidated(TestHarness.StageId).Code);
    }

    [Fact]
    public async Task It_says_when_naming_a_cohorte_is_required_rather_than_optional()
    {
        var (db, current) = await SeedAsync("reval-ctx-fallback");
        await using var _ = db;

        // The roster this registration sits in DOES run the stage here, so the command's fallback
        // resolves and the dialog may leave the field empty.
        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        ctx.FallbackCohortId.Should().NotBeNull();
    }

    [Fact]
    public async Task A_student_whose_roster_does_not_run_the_stage_has_no_fallback()
    {
        // The ordinary case for a revalidation: a 6ᵉ année student redoing a 3ᵉ année stage holds no
        // roster that runs it. Naming a cohorte is then required, and the dialog has to know —
        // offering the act anyway is what NoGroupForRevalidation then refuses. Measured on the live
        // base: Jad Abdallah, 6ᵉ année 2026-2027, is exactly this.
        var (db, current) = await SeedAsync("reval-ctx-no-fallback", currentHasRoster: false);
        await using var _ = db;

        var ctx = (await Handler(db).Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None)).Value;

        ctx.FallbackCohortId.Should().BeNull();
    }

    [Fact]
    public async Task It_is_scolarites_read_not_a_strangers()
    {
        var (db, current) = await SeedAsync("reval-ctx-role");
        await using var _ = db;

        var handler = new GetRevalidationContextQueryHandler(
            db, new WorkingDayProvider(db), db.StrangerAuthorizer());

        var result = await handler.Handle(
            new GetRevalidationContextQuery(current, TestHarness.StageId, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(StageErrors.RevalidationNotAllowed.Code);
    }
}
