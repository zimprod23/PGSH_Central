using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Students.Registrations.Holds;
using PGSH.Domain.Registrations;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The worklist and the release — the half that makes a signalement an act rather than a label.
///
/// <para>The réinscription roll raises ~1 450 holds in one upload. Without a page that lists them and
/// a command that clears them one at a time, freezing a registration is just a silent exclusion,
/// which is the failure the whole mechanism replaces.</para>
/// </summary>
public class RegistrationHoldWorkflowTests
{
    private static readonly DateTime RaisedOn = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    private static GetRegistrationHoldsQueryHandler Worklist(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static ReleaseRegistrationHoldCommandHandler Releaser(
        ApplicationDbContext db, Guid? userId = null) =>
        new(db,
            TestHarness.UserContext(userId ?? Guid.NewGuid(), Roles.Scolarite),
            db.AdminAuthorizer());

    private static Registration Held(
        ApplicationDbContext db,
        string firstName,
        RegistrationHoldReason reason,
        string evidence,
        int academicYearId = TestHarness.CurrentYearId)
    {
        var registration = db.SeedRegistration(firstName, "Signale", academicYearId: academicYearId);
        registration.PlaceOnHold(reason, evidence, RaisedOn).IsSuccess.Should().BeTrue();
        return registration;
    }

    // ---------------------------------------------------------------------------------------------
    // The worklist
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Active by default: the worklist is « qui est encore gelé », not the audit trail. It carries the
    /// evidence and the remedy, because a row naming neither is one nobody can act on.
    /// </summary>
    [Fact]
    public async Task The_worklist_lists_the_standing_holds_with_their_evidence()
    {
        await using var db = TestHarness.NewContext(nameof(The_worklist_lists_the_standing_holds_with_their_evidence));
        db.SeedCatalog();

        Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages,
            "2 stage(s) antérieur(s) non validés — Cardiologie (3ᵉ année).");
        db.SeedRegistration("Salma", "Libre");
        await db.SaveChangesAsync();

        var result = await Worklist(db).Handle(new GetRegistrationHoldsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1, "the unheld registration is not on the worklist");

        var row = result.Value.Items.Single();
        row.Reason.Should().Be(RegistrationHoldReason.OutstandingPriorStages);
        row.ReasonLabel.Should().Be("stages antérieurs non validés");
        row.Evidence.Should().Contain("Cardiologie");
        row.Remedy.Should().NotBeNullOrWhiteSpace();
        row.ReleasedOn.Should().BeNull();
    }

    /// <summary>
    /// ⚠ Scoped by the <b>registration's</b> academic year. One roll raises holds on the closing
    /// year's registrations and creates the opening year's in the same act, so a filter keyed on when
    /// the flag was written would mix two promotions — the same defect as reading a période's year
    /// off its dates instead of off its registration.
    /// </summary>
    [Fact]
    public async Task The_worklist_is_scoped_by_the_registrations_year_not_the_flags_date()
    {
        await using var db = TestHarness.NewContext(nameof(The_worklist_is_scoped_by_the_registrations_year_not_the_flags_date));
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        Held(db, "Amine", RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.");
        Held(db, "Karim", RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent.",
            academicYearId: TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        // Both flags were raised at the same instant; only the registrations differ.
        var current = await Worklist(db).Handle(new GetRegistrationHoldsQuery(), CancellationToken.None);
        var previous = await Worklist(db).Handle(
            new GetRegistrationHoldsQuery(AcademicYearId: TestHarness.PreviousYearId), CancellationToken.None);

        current.Value.TotalCount.Should().Be(1);
        previous.Value.TotalCount.Should().Be(1);
        current.Value.Items.Single().StudentId
            .Should().NotBe(previous.Value.Items.Single().StudentId);
    }

    [Fact]
    public async Task The_worklist_can_be_narrowed_to_one_reason()
    {
        await using var db = TestHarness.NewContext(nameof(The_worklist_can_be_narrowed_to_one_reason));
        db.SeedCatalog();

        Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        Held(db, "Salma", RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absente.");
        await db.SaveChangesAsync();

        var result = await Worklist(db).Handle(
            new GetRegistrationHoldsQuery(Reason: RegistrationHoldReason.AbsentFromReinscriptionRoll),
            CancellationToken.None);

        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Single().Reason
            .Should().Be(RegistrationHoldReason.AbsentFromReinscriptionRoll);
    }

    // ---------------------------------------------------------------------------------------------
    // Releasing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Releasing_puts_the_registration_back_into_planning()
    {
        await using var db = TestHarness.NewContext(nameof(Releasing_puts_the_registration_back_into_planning));
        db.SeedCatalog();

        var registration = Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        await db.SaveChangesAsync();

        var holdId = registration.Holds.Single().Id;
        var actor = Guid.NewGuid();

        var result = await Releaser(db, actor).Handle(
            new ReleaseRegistrationHoldCommand(holdId, "Évaluations saisies : tout est validé."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StillHeld.Should().Be(0);
        result.Value.Released.Should().Be(RegistrationHoldReason.OutstandingPriorStages);

        var reloaded = await db.Registrations.Include(r => r.Holds)
            .SingleAsync(r => r.Id == registration.Id);

        reloaded.IsOnHold.Should().BeFalse();
        reloaded.Holds.Single().ReleasedByUserId.Should().Be(actor);
        reloaded.Holds.Single().ReleaseNote.Should().Contain("Évaluations");
    }

    /// <summary>
    /// ⚠ <c>StillHeld</c> is the difference between « c'est réglé » and « il en reste un », which the
    /// caller cannot work out from a 204. Two reasons are two questions and are cleared separately.
    /// </summary>
    [Fact]
    public async Task Releasing_one_of_two_reports_the_registration_is_still_held()
    {
        await using var db = TestHarness.NewContext(nameof(Releasing_one_of_two_reports_the_registration_is_still_held));
        db.SeedCatalog();

        var registration = Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        registration.PlaceOnHold(
            RegistrationHoldReason.AbsentFromReinscriptionRoll, "Absent du fichier.", RaisedOn)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var debt = registration.Holds
            .Single(h => h.Reason == RegistrationHoldReason.OutstandingPriorStages);

        var result = await Releaser(db).Handle(
            new ReleaseRegistrationHoldCommand(debt.Id, "Stages revalidés."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StillHeld.Should().Be(1);

        (await db.Registrations.Include(r => r.Holds).SingleAsync(r => r.Id == registration.Id))
            .IsOnHold.Should().BeTrue();
    }

    /// <summary>
    /// ⚠ <b>Releasing the blocking flag frees the student even though he stays on the worklist.</b>
    /// « Dossier à compléter » is advisory: he plans while somebody finishes his file. Reporting him
    /// as still frozen would send the operator chasing a block that is not there — which is why
    /// <c>StillBlocked</c> exists beside <c>StillHeld</c>.
    /// </summary>
    [Fact]
    public async Task Releasing_the_blocking_flag_frees_a_student_who_stays_flagged()
    {
        await using var db = TestHarness.NewContext(nameof(Releasing_the_blocking_flag_frees_a_student_who_stays_flagged));
        db.SeedCatalog();

        var registration = Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        registration.PlaceOnHold(
            RegistrationHoldReason.IncompleteStudentFile, "Fiche à compléter.", RaisedOn)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var debt = registration.Holds
            .Single(h => h.Reason == RegistrationHoldReason.OutstandingPriorStages);

        var result = await Releaser(db).Handle(
            new ReleaseRegistrationHoldCommand(debt.Id, "Stages validés."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StillHeld.Should().Be(1, "his file is still incomplete");
        result.Value.StillBlocked.Should().BeFalse("but nothing left blocks planning");

        var reloaded = await db.Registrations.Include(r => r.Holds)
            .SingleAsync(r => r.Id == registration.Id);

        reloaded.IsOnHold.Should().BeFalse();
        reloaded.IsFlagged.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_hold_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_hold_is_refused));
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Releaser(db).Handle(
            new ReleaseRegistrationHoldCommand(Guid.NewGuid(), "Vérifié."), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.NotFound");
    }

    /// <summary>
    /// Only scolarité lifts a signalement. ⚠ Asserted with the store checked afterwards: a guard
    /// placed after the write returns the same failure and would pass an assertion on the Result
    /// alone.
    /// </summary>
    [Fact]
    public async Task A_stranger_cannot_release_a_hold()
    {
        await using var db = TestHarness.NewContext(nameof(A_stranger_cannot_release_a_hold));
        db.SeedCatalog();

        var registration = Held(db, "Amine", RegistrationHoldReason.OutstandingPriorStages, "Dette.");
        await db.SaveChangesAsync();

        var handler = new ReleaseRegistrationHoldCommandHandler(
            db, TestHarness.UserContext(Guid.NewGuid()), db.StrangerAuthorizer());

        var result = await handler.Handle(
            new ReleaseRegistrationHoldCommand(registration.Holds.Single().Id, "Vérifié."),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegistrationHolds.NotAllowed");

        (await db.Registrations.Include(r => r.Holds).SingleAsync(r => r.Id == registration.Id))
            .IsOnHold.Should().BeTrue("nothing was written");
    }
}
