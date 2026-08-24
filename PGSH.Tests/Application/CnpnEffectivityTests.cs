using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Cnpn.Effectivity;
using PGSH.Application.Stages.Cnpn.Manage;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Students.Registrations.Create;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « À partir de la 3ᵉ année de 2026-2027 » — a text taking over a level from a year onward, whoever
/// is sitting in it.
///
/// <para>The two cases that decide the whole design pull in opposite directions, and both are real:
/// the <b>repeater</b> re-registering in the named level must be moved onto the new text even though
/// he entered years earlier, while the student who has <b>moved on</b> and still owes stages from
/// that level must keep the text he sat it under. Only a stamp frozen onto each registration can be
/// true of both at once — which is what these tests are here to hold.</para>
/// </summary>
public class CnpnEffectivityTests
{
    private const int OldText = TestHarness.OldCnpnId;   // 2174.18, seven years
    private const int NewText = TestHarness.NewCnpnId;   // 1650.25, six years

    private const int Year2024 = 21;   // 2024-2025
    private const int Year2025 = TestHarness.CurrentYearId;  // 2025-2026
    private const int Year2026 = 23;   // 2026-2027

    private const int Level3 = TestHarness.LevelId;  // 3ème année
    private const int Level4 = 24;
    private const int Level1 = 25;

    /// <summary>
    /// Three years, three promotions, and the two texts with real intake years to select between —
    /// the shape of the transition the arrêté actually describes.
    /// </summary>
    private static ApplicationDbContext Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        db.SeedCatalog();
        db.SeedAcademicYear(Year2024, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedAcademicYear(Year2026, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        db.SeedLevel(Level4, "4ème année", year: 4);
        db.SeedLevel(Level1, "1ère année", year: 1);

        // SeedCatalog's pair governs no intake / the current one; these need dated intakes.
        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == OldText));
        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == NewText));
        db.SeedCnpnVersion(OldText, "2174.18", totalYears: 7, appliesFromAcademicYearId: Year2024);
        db.SeedCnpnVersion(NewText, "1650.25", totalYears: 6, appliesFromAcademicYearId: Year2026);

        db.SaveChanges();
        return db;
    }

    private static Student AddStudent(ApplicationDbContext db, string lastName, int? stamp = null)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Amine", LastName = lastName,
            Email = $"{lastName}@etu.ma".ToLowerInvariant(),
            CNE = $"CNE{Guid.NewGuid():N}"[..10], Appogee = $"AP{Guid.NewGuid():N}"[..8],
            BacYear = "2021", AcademicProgram = AcademicProgram.Medecine,
        };

        if (stamp is { } versionId)
            student.AssignCnpnVersion(versionId, isInferred: false);

        db.Users.Add(student);
        return student;
    }

    private static Registration AddRegistration(
        ApplicationDbContext db, Student student, int levelId, int yearId,
        int? cnpnVersionId = null, RegistrationStatus status = RegistrationStatus.Active)
    {
        var registration = new Registration
        {
            Id = Guid.NewGuid(), StudentId = student.Id, Student = student,
            LevelId = levelId, AcademicYearId = yearId, Status = status,
        };

        if (cnpnVersionId is { } versionId)
            registration.StampCnpnVersion(versionId, RegistrationCnpnSource.Backfilled);

        db.Registrations.Add(registration);
        return registration;
    }

    private static RegistrationCnpnStamper Stamper(ApplicationDbContext db) =>
        new(db, new CnpnAssignment(db));

    private static FinalYearGuard Guard(ApplicationDbContext db) =>
        new(db, new OutstandingStageFinder(db));

    // =============================================================================================
    // The two halves of the rule
    // =============================================================================================

    /// <summary>
    /// The case the whole model exists for. A student sat the 3ᵉ année in 2024-2025 under the old
    /// text, moved up to the 4ᵉ, and still owes two stages from that year. The faculty then reshapes
    /// the 3ᵉ année from 2026-2027. What he owes must not move: he is judged against the text of the
    /// year he sat, not against the level as it stands now.
    /// </summary>
    [Fact]
    public async Task A_closed_year_keeps_the_text_it_was_sat_under_when_the_level_is_reshaped()
    {
        using var db = Seed(nameof(A_closed_year_keeps_the_text_it_was_sat_under_when_the_level_is_reshaped));

        var student = AddStudent(db, "Bennani", stamp: OldText);
        var third = AddRegistration(db, student, Level3, Year2024, cnpnVersionId: OldText);
        AddRegistration(db, student, Level4, Year2026, cnpnVersionId: OldText);
        await db.SaveChangesAsync();

        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var planner = new CnpnEffectivityPlanner(db, Stamper(db));
        var plan = await planner.PlanAsync(1, default);
        plan.IsSuccess.Should().BeTrue();

        // The 2024-2025 registration is before the rule takes effect, so it is not even in scope.
        plan.Value.Preview.InScope.Should().Be(0);

        var reloaded = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == third.Id);
        reloaded.CnpnVersionId.Should().Be(OldText);
    }

    /// <summary>
    /// The other half, and the one an entry-based rule cannot express: two students with the same
    /// entry year, one repeating the named level and one a year ahead of it, land on different texts.
    /// </summary>
    [Fact]
    public async Task A_repeater_re_registering_in_the_named_level_is_moved_onto_the_new_text()
    {
        using var db = Seed(nameof(A_repeater_re_registering_in_the_named_level_is_moved_onto_the_new_text));

        var repeater = AddStudent(db, "Alaoui", stamp: OldText);
        AddRegistration(db, repeater, Level3, Year2025, cnpnVersionId: OldText,
            status: RegistrationStatus.Failed);

        var aheadOfHim = AddStudent(db, "Chraibi", stamp: OldText);
        AddRegistration(db, aheadOfHim, Level3, Year2025, cnpnVersionId: OldText,
            status: RegistrationStatus.Validated);

        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new CreateRegistrationCommandHandler(db, Stamper(db), Guard(db));

        var repeat = await handler.Handle(
            new CreateRegistrationCommand(repeater.Id, Year2026, Level3, RegistrationStatus.Active), default);

        var promoted = await handler.Handle(
            new CreateRegistrationCommand(aheadOfHim.Id, Year2026, Level4, RegistrationStatus.Active), default);

        repeat.IsSuccess.Should().BeTrue();
        promoted.IsSuccess.Should().BeTrue();

        var repeated = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == repeat.Value);
        var moved = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == promoted.Value);

        repeated.CnpnVersionId.Should().Be(NewText, "the rule names his level and his year");
        repeated.CnpnSource.Should().Be(RegistrationCnpnSource.Effectivity);

        moved.CnpnVersionId.Should().Be(OldText, "no rule names the 4ᵉ année, so he keeps his text");
        moved.CnpnSource.Should().Be(RegistrationCnpnSource.StudentStamp);
    }

    /// <summary>
    /// A rule is the faculty saying "these people are now on this text", so it has to reach the
    /// student's own stamp — otherwise <c>TotalYears</c>, and therefore how many years he owes, would
    /// still be read from the text he just left. It overrides a <i>confirmed</i> stamp, which bulk
    /// targeting deliberately refuses to do.
    /// </summary>
    [Fact]
    public async Task An_effectivity_rule_advances_the_students_own_stamp()
    {
        using var db = Seed(nameof(An_effectivity_rule_advances_the_students_own_stamp));

        var student = AddStudent(db, "Idrissi", stamp: OldText);
        AddRegistration(db, student, Level3, Year2025, cnpnVersionId: OldText);
        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new CreateRegistrationCommandHandler(db, Stamper(db), Guard(db));
        var created = await handler.Handle(
            new CreateRegistrationCommand(student.Id, Year2026, Level3, RegistrationStatus.Active), default);

        created.IsSuccess.Should().BeTrue();

        var reloaded = await db.Users.OfType<Student>().AsNoTracking().FirstAsync(s => s.Id == student.Id);
        reloaded.CnpnVersionId.Should().Be(NewText);
        reloaded.CnpnAssignmentIsInferred.Should().BeFalse();
    }

    // =============================================================================================
    // Resolution order
    // =============================================================================================

    /// <summary>Nobody has ruled on this level, so the student keeps the text he already followed.</summary>
    [Fact]
    public async Task Without_a_rule_the_registration_takes_the_students_stamp()
    {
        using var db = Seed(nameof(Without_a_rule_the_registration_takes_the_students_stamp));

        var student = AddStudent(db, "Fassi", stamp: OldText);
        await db.SaveChangesAsync();

        var registration = AddRegistration(db, student, Level3, Year2026);
        var report = await Stamper(db).StampAsync([registration], default);

        report.IsSuccess.Should().BeTrue();
        registration.CnpnVersionId.Should().Be(OldText);
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.StudentStamp);
    }

    /// <summary>
    /// Stickiness lives in the parcours, not only in the denormalised field: a student with no stamp
    /// of his own but a stamped year behind him carries that text forward rather than being
    /// re-resolved from his intake, which could land him somewhere else entirely.
    /// </summary>
    [Fact]
    public async Task Without_a_stamp_the_text_is_carried_from_the_most_recent_year()
    {
        using var db = Seed(nameof(Without_a_stamp_the_text_is_carried_from_the_most_recent_year));

        var student = AddStudent(db, "Guessous");
        AddRegistration(db, student, Level3, Year2024, cnpnVersionId: NewText);
        await db.SaveChangesAsync();

        var registration = AddRegistration(db, student, Level4, Year2026);
        var report = await Stamper(db).StampAsync([registration], default);

        report.IsSuccess.Should().BeTrue();
        registration.CnpnVersionId.Should().Be(NewText);
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.CarriedForward);
    }

    /// <summary>
    /// A genuine new entrant. His first registration has not been saved, so there is no recorded
    /// entry to read — the registration being created is its own evidence, and the text governing
    /// that intake is the answer.
    /// </summary>
    [Fact]
    public async Task A_first_registration_resolves_the_text_governing_its_own_intake()
    {
        using var db = Seed(nameof(A_first_registration_resolves_the_text_governing_its_own_intake));

        var student = AddStudent(db, "Haddad");
        await db.SaveChangesAsync();

        var handler = new CreateRegistrationCommandHandler(db, Stamper(db), Guard(db));
        var created = await handler.Handle(
            new CreateRegistrationCommand(student.Id, Year2026, Level1, RegistrationStatus.Active), default);

        created.IsSuccess.Should().BeTrue();

        var registration = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == created.Value);
        registration.CnpnVersionId.Should().Be(NewText, "1650.25 governs entrants from 2026-2027");
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.ResolvedFromEntry);
    }

    /// <summary>
    /// No rule, no stamp, no history and no text reaching back to the intake. The registration is
    /// created and simply carries no text — stamping it with a guess would put the guess beyond the
    /// reach of the correction path, and every reader already falls back to the student.
    /// </summary>
    [Fact]
    public async Task An_unresolvable_registration_is_created_without_a_text_rather_than_refused()
    {
        using var db = Seed(nameof(An_unresolvable_registration_is_created_without_a_text_rather_than_refused));

        var pharmacist = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Nadia", LastName = "Sekkat",
            Email = "sekkat@etu.ma", CNE = "CNE00000X", Appogee = "AP00000X",
            BacYear = "2025", AcademicProgram = AcademicProgram.Pharmacie,
        };
        db.Users.Add(pharmacist);
        db.SeedLevel(60, "1ère année Pharmacie", year: 1, program: AcademicProgram.Pharmacie);
        await db.SaveChangesAsync();

        var handler = new CreateRegistrationCommandHandler(db, Stamper(db), Guard(db));
        var created = await handler.Handle(
            new CreateRegistrationCommand(pharmacist.Id, Year2026, 60, RegistrationStatus.Active), default);

        created.IsSuccess.Should().BeTrue("no Pharmacie text is recorded, which is not the student's fault");

        var registration = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == created.Value);
        registration.CnpnVersionId.Should().BeNull();
        registration.CnpnSource.Should().BeNull();
    }

    // =============================================================================================
    // Authoring a rule
    // =============================================================================================

    [Fact]
    public async Task A_rule_is_recorded_for_a_level_of_the_texts_own_programme()
    {
        using var db = Seed(nameof(A_rule_is_recorded_for_a_level_of_the_texts_own_programme));

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(
            new CreateCnpnEffectivityCommand(NewText, Level3, Year2026, "Après négociation"), default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.CnpnLevelEffectivities.AsNoTracking().SingleAsync();
        stored.CnpnVersionId.Should().Be(NewText);
        stored.LevelId.Should().Be(Level3);
        stored.Note.Should().Be("Après négociation");
    }

    [Fact]
    public async Task A_rule_pairing_a_text_with_another_programmes_level_is_refused()
    {
        using var db = Seed(nameof(A_rule_pairing_a_text_with_another_programmes_level_is_refused));
        db.SeedLevel(61, "2ème année Pharmacie", year: 2, program: AcademicProgram.Pharmacie);
        await db.SaveChangesAsync();

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new CreateCnpnEffectivityCommand(NewText, 61, Year2026, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnEffectivity.ProgramMismatch");
    }

    /// <summary>« Retrait » has nobody to govern — the same guard the partition cut and auto-arrange apply.</summary>
    [Fact]
    public async Task A_rule_on_the_withdrawal_marker_is_refused()
    {
        using var db = Seed(nameof(A_rule_on_the_withdrawal_marker_is_refused));
        db.SeedLevel(62, "Retrait", year: 0);
        await db.SaveChangesAsync();

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new CreateCnpnEffectivityCommand(NewText, 62, Year2026, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotAPromotion");
    }

    [Fact]
    public async Task A_text_cannot_take_effect_twice_for_one_level()
    {
        using var db = Seed(nameof(A_text_cannot_take_effect_twice_for_one_level));
        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new CreateCnpnEffectivityCommand(NewText, Level3, Year2025, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnEffectivity.AlreadyDeclared");
    }

    /// <summary>
    /// Two texts starting to govern one level in one year. Resolution takes the latest start date at
    /// or before the year, so a tie has no defensible winner — the same objection as two texts
    /// claiming one intake.
    /// </summary>
    [Fact]
    public async Task Two_texts_cannot_take_effect_for_one_level_in_one_year()
    {
        using var db = Seed(nameof(Two_texts_cannot_take_effect_for_one_level_in_one_year));
        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new CreateCnpnEffectivityCommand(OldText, Level3, Year2026, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnEffectivity.YearAlreadyTaken");
    }

    /// <summary>A six-year text cannot take effect for a seventh year — there is no such year in it.</summary>
    [Fact]
    public async Task A_rule_beyond_the_texts_span_is_refused()
    {
        using var db = Seed(nameof(A_rule_beyond_the_texts_span_is_refused));
        db.SeedLevel(63, "7ème année", year: 7);
        await db.SaveChangesAsync();

        var handler = new CreateCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new CreateCnpnEffectivityCommand(NewText, 63, Year2026, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CannotShortenBelowEffectiveLevel");
    }

    // =============================================================================================
    // Applying a rule authored too late
    // =============================================================================================

    /// <summary>
    /// The rollover ran in September and the faculty settled the cut in October. The registrations
    /// already exist, so the rule has to be applied to them explicitly.
    /// </summary>
    [Fact]
    public async Task Applying_a_late_rule_moves_the_registrations_it_names()
    {
        using var db = Seed(nameof(Applying_a_late_rule_moves_the_registrations_it_names));

        var student = AddStudent(db, "Jaidi", stamp: OldText);
        var registration = AddRegistration(db, student, Level3, Year2026, cnpnVersionId: OldText);
        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var planner = new CnpnEffectivityPlanner(db, Stamper(db));
        var handler = new ApplyCnpnEffectivityCommandHandler(db, planner, db.AdminAuthorizer());

        var preview = await planner.PlanAsync(1, default);
        preview.Value.Preview.WillMove.Should().Be(1);
        preview.Value.Preview.CanApply.Should().BeTrue();

        var applied = await handler.Handle(new ApplyCnpnEffectivityCommand(1, ConfirmedMoveCount: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var reloaded = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == registration.Id);
        reloaded.CnpnVersionId.Should().Be(NewText);
        reloaded.CnpnSource.Should().Be(RegistrationCnpnSource.Effectivity);
    }

    /// <summary>
    /// A verdict was recorded against a requirement set. Moving that set afterwards leaves nobody able
    /// to say what the jury ruled on, so the row is reported and left alone — there is no override.
    /// </summary>
    [Fact]
    public async Task Applying_a_late_rule_refuses_a_year_already_pronounced()
    {
        using var db = Seed(nameof(Applying_a_late_rule_refuses_a_year_already_pronounced));

        var student = AddStudent(db, "Kettani", stamp: OldText);
        var registration = AddRegistration(db, student, Level3, Year2026, cnpnVersionId: OldText);
        registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);

        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var planner = new CnpnEffectivityPlanner(db, Stamper(db));
        var plan = await planner.PlanAsync(1, default);

        plan.Value.Preview.FrozenByOutcome.Should().Be(1);
        plan.Value.Preview.WillMove.Should().Be(0);
        plan.Value.Preview.CanApply.Should().BeFalse();

        var reloaded = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == registration.Id);
        reloaded.CnpnVersionId.Should().Be(OldText);
    }

    /// <summary>
    /// A registration created between the preview and the apply widens the act silently. The confirmed
    /// count is what notices — the same guard the déliberation's <c>ConfirmedDefaultCount</c> is.
    /// </summary>
    [Fact]
    public async Task Applying_a_late_rule_refuses_a_stale_confirmation()
    {
        using var db = Seed(nameof(Applying_a_late_rule_refuses_a_stale_confirmation));

        var first = AddStudent(db, "Lahlou", stamp: OldText);
        AddRegistration(db, first, Level3, Year2026, cnpnVersionId: OldText);
        var second = AddStudent(db, "Mernissi", stamp: OldText);
        AddRegistration(db, second, Level3, Year2026, cnpnVersionId: OldText);

        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new ApplyCnpnEffectivityCommandHandler(
            db, new CnpnEffectivityPlanner(db, Stamper(db)), db.AdminAuthorizer());

        var result = await handler.Handle(new ApplyCnpnEffectivityCommand(1, ConfirmedMoveCount: 1), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CnpnEffectivity.MoveCountNotConfirmed");
    }

    // =============================================================================================
    // Removing a rule, and removing a text
    // =============================================================================================

    /// <summary>
    /// Deleting a rule is prospective. The registrations it already stamped keep their text — students
    /// have been studying against it — and the count is returned so the confirmation can say so.
    /// </summary>
    [Fact]
    public async Task Deleting_a_rule_leaves_the_registrations_it_stamped_alone()
    {
        using var db = Seed(nameof(Deleting_a_rule_leaves_the_registrations_it_stamped_alone));

        var student = AddStudent(db, "Naciri", stamp: NewText);
        var registration = AddRegistration(db, student, Level3, Year2026, cnpnVersionId: NewText);
        db.SeedEffectivity(1, NewText, Level3, Year2026);
        await db.SaveChangesAsync();

        var handler = new DeleteCnpnEffectivityCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new DeleteCnpnEffectivityCommand(1), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1, "one registration was governed by the rule");

        (await db.CnpnLevelEffectivities.CountAsync()).Should().Be(0);

        var reloaded = await db.Registrations.AsNoTracking().FirstAsync(r => r.Id == registration.Id);
        reloaded.CnpnVersionId.Should().Be(NewText);
    }

    /// <summary>
    /// The student gate is not enough on its own: a text can govern a closed year of a student who has
    /// since moved to another one, so the student count reaches zero while registrations still name it.
    /// Those rows are the record of what those years required.
    /// </summary>
    [Fact]
    public async Task A_text_still_named_by_a_registration_cannot_be_deleted()
    {
        using var db = Seed(nameof(A_text_still_named_by_a_registration_cannot_be_deleted));

        var student = AddStudent(db, "Ouazzani", stamp: NewText);
        AddRegistration(db, student, Level3, Year2024, cnpnVersionId: OldText);
        await db.SaveChangesAsync();

        var handler = new DeleteCnpnVersionCommandHandler(db, db.AdminAuthorizer());
        var result = await handler.Handle(new DeleteCnpnVersionCommand(OldText), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.CannotDeleteWithRegistrations");
    }
}
