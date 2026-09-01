using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Students;
using PGSH.Application.Students.Registrations.Inscription;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The third act of the year. The déliberation writes verdicts onto registrations of the closing
/// year; the réinscription reads those verdicts and creates next year's registrations. Both start
/// from a registration the student already holds, which is exactly why neither can see the people
/// this act exists for — the September intake, transfers arriving from another faculty, returners
/// and réorientations. They hold no registration to be read.
/// </summary>
public class InscriptionTests
{
    /// <summary>« 1ère année Médecine » — where an intake actually lands. <c>SeedCatalog</c>'s level
    /// is a 3ᵉ année, which is the transfer case, not the entrant one.</summary>
    private const int FirstYearLevelId = 40;

    /// <summary>« 1ère année Pharmacie » — the other programme, for the réorientation.</summary>
    private const int PharmacyLevelId = 41;

    private static InscriptionPlanner Planner(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new FinalYearGuard(db, new OutstandingStageFinder(db)));

    private static InscriptionApplier Applier(ApplicationDbContext db) =>
        new(db, new RegistrationCnpnStamper(db, new CnpnAssignment(db)), db.AdminAuthorizer());

    private static ApplyInscriptionCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(Planner(db), Applier(db), db.AdminAuthorizer());

    private static InscribeStudentCommandHandler SingleHandler(ApplicationDbContext db) =>
        new(Planner(db), Applier(db), db.AdminAuthorizer());

    private static PreviewInscriptionQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(Planner(db), db.AdminAuthorizer());

    private static void SeedPromotions(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedLevel(FirstYearLevelId, "1ère année", year: 1);
        db.SeedLevel(PharmacyLevelId, "1ère année Pharmacie", year: 1, program: AcademicProgram.Pharmacie);
    }

    /// <summary>The minimum a row needs to create somebody: an identifier and a name.</summary>
    private static InscriptionRow Row(
        int sheetRow, string cne, string firstName, string lastName,
        string? appogee = null, string? email = null,
        string? institution = null, string? lastYear = null, string? reference = null) =>
        new(sheetRow, cne, appogee, lastName, firstName, null, email, "M", null, null, "2025", "SVT",
            null, null, institution, null, lastYear, reference, null);

    private static ApplyInscriptionCommand Apply(
        IReadOnlyList<InscriptionRow> rows, int levelId = FirstYearLevelId, int? confirmed = null) =>
        new(rows, levelId, null, confirmed);

    // ---------------------------------------------------------------------------------------------
    // The intake
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_first_year_becomes_a_student_and_a_registration()
    {
        await using var db = TestHarness.NewContext(nameof(A_new_first_year_becomes_a_student_and_a_registration));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "R130896", "Yasmine", "Idrissi", email: "y.idrissi@um5.ac.ma") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.NewEntrants.Should().Be(1);
        preview.Value.WillCreateStudents.Should().Be(1);
        preview.Value.CanApply.Should().BeTrue();

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var student = await db.Students.SingleAsync(s => s.CNE == "R130896");
        student.FirstName.Should().Be("Yasmine");
        student.AcademicProgram.Should().Be(AcademicProgram.Medecine);

        var registration = await db.Registrations.SingleAsync(r => r.StudentId == student.Id);
        registration.LevelId.Should().Be(FirstYearLevelId);
        registration.AcademicYearId.Should().Be(TestHarness.CurrentYearId);
        registration.Status.Should().Be(RegistrationStatus.Pending);
    }

    /// <summary>
    /// The whole reason this act is separate: the intake list is the one document that names people
    /// PGSH has never heard of, and no other path creates a student in bulk.
    /// </summary>
    [Fact]
    public async Task The_preview_is_the_plan_and_nothing_is_written_by_it()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_is_the_plan_and_nothing_is_written_by_it));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "A1", "Sara", "Bennani", email: "s.b@um5.ac.ma"),
            Row(3, "A2", "Ali", "Amrani", email: "a.a@um5.ac.ma"),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.WillCreateStudents.Should().Be(2);
        (await db.Students.CountAsync()).Should().Be(0);
        (await db.Registrations.CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Creating people is confirmed by a number, never by a flag
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Applying_without_confirming_the_number_of_new_students_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Applying_without_confirming_the_number_of_new_students_is_refused));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "A1", "Sara", "Bennani", email: "s.b@um5.ac.ma") };

        var applied = await ApplyHandler(db).Handle(Apply(rows), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("Inscription.CreationsNotConfirmed");
        (await db.Students.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_confirmation_that_no_longer_matches_the_plan_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_confirmation_that_no_longer_matches_the_plan_is_refused));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "A1", "Sara", "Bennani", email: "s.b@um5.ac.ma"),
            Row(3, "A2", "Ali", "Amrani", email: "a.a@um5.ac.ma"),
        };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("Inscription.CreationsNotConfirmed");
        (await db.Students.CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Arriving from another faculty
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ The équivalence is the point of the row, not decoration. Without it the dossier opens in the
    /// middle of a cursus with nothing saying the years below were recognised — and the day « ce qu'il
    /// doit » is read from the CNPN's requirement set rather than from his failed attempts, he owes
    /// every stage of the years he did elsewhere.
    /// </summary>
    [Fact]
    public async Task A_newcomer_above_the_first_year_is_refused_without_a_provenance()
    {
        await using var db = TestHarness.NewContext(nameof(A_newcomer_above_the_first_year_is_refused_without_a_provenance));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "T1", "Omar", "Alaoui", email: "o.a@um5.ac.ma") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.OriginRequired);
        preview.Value.CanApply.Should().BeFalse();

        var applied = await ApplyHandler(db).Handle(
            Apply(rows, TestHarness.LevelId, confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        (await db.Students.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_transfer_records_the_equivalence_against_the_registration_that_admitted_him()
    {
        await using var db = TestHarness.NewContext(nameof(A_transfer_records_the_equivalence_against_the_registration_that_admitted_him));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "T1", "Omar", "Alaoui", email: "o.a@um5.ac.ma",
                institution: "FMP Casablanca", lastYear: "2", reference: "Arrêté 12/2026"),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.TransfersIn.Should().Be(1);
        preview.Value.OriginsRecorded.Should().Be(1);

        var applied = await ApplyHandler(db).Handle(
            Apply(rows, TestHarness.LevelId, confirmed: 1), default);

        applied.IsSuccess.Should().BeTrue();

        var origin = await db.PriorEnrolments.SingleAsync();
        origin.Institution.Should().Be("FMP Casablanca");
        origin.LastLevelYearCompleted.Should().Be(2);
        origin.EquivalenceReference.Should().Be("Arrêté 12/2026");

        var registration = await db.Registrations.SingleAsync();
        origin.RegistrationId.Should().Be(registration.Id);
    }

    /// <summary>Two of the three provenance cells is a record that cannot say what it recognised.</summary>
    [Fact]
    public async Task A_half_filled_provenance_is_refused_rather_than_silently_dropped()
    {
        await using var db = TestHarness.NewContext(nameof(A_half_filled_provenance_is_refused_rather_than_silently_dropped));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "T1", "Omar", "Alaoui", email: "o.a@um5.ac.ma", institution: "FMP Casablanca"),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.InvalidValue);
        preview.Value.CanApply.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // Students PGSH already holds
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Two of the twelve « Retrait » students did exactly this — withdrew, then came back two years
    /// later. The réinscription cannot carry them: they hold no registration in the closing year.
    /// </summary>
    [Fact]
    public async Task A_returning_student_is_registered_without_a_second_student_record()
    {
        await using var db = TestHarness.NewContext(nameof(A_returning_student_is_registered_without_a_second_student_record));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var past = db.SeedRegistration("Hamza", "Tazi", academicYearId: TestHarness.PreviousYearId);
        past.Status = RegistrationStatus.Withdrawn;
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, past.Student!.CNE, "Hamza", "Tazi") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.Returning.Should().Be(1);
        preview.Value.WillCreateStudents.Should().Be(0);

        var applied = await ApplyHandler(db).Handle(Apply(rows, TestHarness.LevelId), default);
        applied.IsSuccess.Should().BeTrue();

        (await db.Students.CountAsync()).Should().Be(1);
        (await db.Registrations.CountAsync(r => r.StudentId == past.StudentId)).Should().Be(2);
    }

    /// <summary>
    /// ⚠ A <c>CnpnVersion</c> belongs to exactly one programme, so a stamp carried across a
    /// réorientation names a text governing a cursus the student has left — and everything reading
    /// <c>TotalYears</c> from it then answers « est-ce sa dernière année ? » from the wrong arrêté.
    /// </summary>
    [Fact]
    public async Task A_reorientation_moves_the_programme_and_does_not_carry_the_old_text()
    {
        await using var db = TestHarness.NewContext(nameof(A_reorientation_moves_the_programme_and_does_not_carry_the_old_text));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedCnpnVersion(93, "PHARM-01", totalYears: 6, program: AcademicProgram.Pharmacie,
            appliesFromAcademicYearId: TestHarness.PreviousYearId);

        var past = db.SeedRegistration("Nadia", "Sekkat", academicYearId: TestHarness.PreviousYearId);
        past.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, past.Student.CNE, "Nadia", "Sekkat") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, PharmacyLevelId), default);

        preview.Value.ProgrammeChanges.Should().Be(1);

        var applied = await ApplyHandler(db).Handle(Apply(rows, PharmacyLevelId), default);
        applied.IsSuccess.Should().BeTrue();

        var student = await db.Students.SingleAsync(s => s.Id == past.StudentId);
        student.AcademicProgram.Should().Be(AcademicProgram.Pharmacie);

        // The Pharmacie text, not the Médecine one he arrived carrying — that governs a cursus he
        // has left, and TotalYears read from it would answer from the wrong arrêté.
        var created = await db.Registrations
            .SingleAsync(r => r.StudentId == past.StudentId && r.LevelId == PharmacyLevelId);
        created.CnpnVersionId.Should().Be(93);
        student.CnpnVersionId.Should().Be(93);
    }

    /// <summary>
    /// ⚠ Unresolved is not « leave it as it was ». Where PGSH holds no text of the new programme
    /// applying at or before this student's entry, the stamp he arrived with is simply false, and
    /// null — « never resolved » — is what is true. Every reader already falls back on it.
    /// </summary>
    [Fact]
    public async Task A_reorientation_with_no_resolvable_text_clears_the_stamp_rather_than_keeping_it()
    {
        await using var db = TestHarness.NewContext(nameof(A_reorientation_with_no_resolvable_text_clears_the_stamp_rather_than_keeping_it));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var past = db.SeedRegistration("Nadia", "Sekkat", academicYearId: TestHarness.PreviousYearId);
        past.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, past.Student.CNE, "Nadia", "Sekkat") };

        var applied = await ApplyHandler(db).Handle(Apply(rows, PharmacyLevelId), default);
        applied.IsSuccess.Should().BeTrue();

        var student = await db.Students.SingleAsync(s => s.Id == past.StudentId);
        student.AcademicProgram.Should().Be(AcademicProgram.Pharmacie);
        student.CnpnVersionId.Should().BeNull();
    }

    /// <summary>
    /// Idempotence, and it is not a nicety: this act creates identities, so the file has to survive
    /// being re-sent with the late arrivals appended.
    /// </summary>
    [Fact]
    public async Task A_student_already_registered_this_year_is_skipped_rather_than_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_already_registered_this_year_is_skipped_rather_than_refused));
        SeedPromotions(db);
        var existing = db.SeedRegistration("Karim", "Fassi");
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, existing.Student!.CNE, "Karim", "Fassi"),
            Row(3, "NEW1", "Leila", "Ouazzani", email: "l.o@um5.ac.ma"),
        };

        var applied = await ApplyHandler(db).Handle(
            Apply(rows, FirstYearLevelId, confirmed: 1), default);

        applied.IsSuccess.Should().BeTrue();
        applied.Value.AlreadyRegistered.Should().Be(1);
        applied.Value.CanApply.Should().BeTrue();

        (await db.Registrations.CountAsync(r => r.StudentId == existing.StudentId)).Should().Be(1);
        (await db.Students.CountAsync()).Should().Be(2);
    }

    // ---------------------------------------------------------------------------------------------
    // Identity
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ An address is a login: <c>SyncUserMiddleware</c> falls back to matching a Keycloak account on
    /// e-mail. A manufactured one that somebody already holds hands a student another person's
    /// account, so the taken set is read from the store and not merely from the batch.
    /// </summary>
    [Fact]
    public async Task A_generated_address_never_collides_with_one_already_in_the_store()
    {
        await using var db = TestHarness.NewContext(nameof(A_generated_address_never_collides_with_one_already_in_the_store));
        SeedPromotions(db);

        db.Users.Add(new Student
        {
            Id = Guid.NewGuid(), FirstName = "Sara", LastName = "Bennani",
            Email = "sara_bennani@um5.ac.ma", CNE = "OLD-1", Appogee = "OLD-AP-1", BacYear = "2020",
        });
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "NEW-1", "Sara", "Bennani") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.GeneratedEmails.Should().Be(1);
        preview.Value.Rows.Single().GeneratedEmail.Should().Be("sara_bennani2@um5.ac.ma");

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        (await db.Students.CountAsync(s => s.Email == "sara_bennani@um5.ac.ma")).Should().Be(1);
        (await db.Students.CountAsync(s => s.Email == "sara_bennani2@um5.ac.ma")).Should().Be(1);
    }

    /// <summary>Two identical names in one intake must not be handed the same address either.</summary>
    [Fact]
    public async Task Two_homonyms_in_one_file_get_distinct_generated_addresses()
    {
        await using var db = TestHarness.NewContext(nameof(Two_homonyms_in_one_file_get_distinct_generated_addresses));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "H1", "Mohamed", "Alaoui"),
            Row(3, "H2", "Mohamed", "Alaoui"),
        };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 2), default);
        applied.IsSuccess.Should().BeTrue();

        var emails = await db.Students.Select(s => s.Email).ToListAsync();
        emails.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// ⚠ <b>CNE and Apogée are both NOT NULL UNIQUE.</b> <c>IX_Student_Appogee</c> carries a
    /// « WHERE Appogee IS NOT NULL » filter that reads as though absence were allowed, but the column
    /// itself is required — so "" is a *value* and the second student without an Apogée would collide
    /// with the first. Whichever identifier the row omits is manufactured from the other.
    /// </summary>
    [Fact]
    public async Task A_row_carrying_only_one_identifier_gets_a_provisional_value_for_the_other()
    {
        await using var db = TestHarness.NewContext(nameof(A_row_carrying_only_one_identifier_gets_a_provisional_value_for_the_other));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "N1", "Sara", "Bennani"),
            Row(3, "N2", "Ali", "Amrani"),
        };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 2), default);
        applied.IsSuccess.Should().BeTrue();

        var created = await db.Students.Where(s => s.CNE == "N1" || s.CNE == "N2").ToListAsync();
        created.Select(s => s.Appogee).Should().BeEquivalentTo(["SANS-APOGEE-N1", "SANS-APOGEE-N2"]);
    }

    /// <summary>
    /// <c>Students.CNE</c> is NOT NULL UNIQUE and an international student legitimately has none —
    /// the legacy import hit the same wall on 4 693 of 10 203 rows.
    /// </summary>
    [Fact]
    public async Task A_row_identified_only_by_its_apogee_number_gets_a_provisional_cne()
    {
        await using var db = TestHarness.NewContext(nameof(A_row_identified_only_by_its_apogee_number_gets_a_provisional_cne));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, null!, "Ines", "Berrada", appogee: "AP99001") };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var student = await db.Students.SingleAsync(s => s.Appogee == "AP99001");
        student.CNE.Should().Be("SANS-CNE-AP99001");
    }

    // ---------------------------------------------------------------------------------------------
    // Refusals
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// All four identifiers are unique in the store, so picking one over the other either creates the
    /// wrong person or fails at SaveChanges with nothing actionable in the message.
    /// </summary>
    [Fact]
    public async Task Two_identifiers_pointing_at_two_different_students_refuse_the_row()
    {
        await using var db = TestHarness.NewContext(nameof(Two_identifiers_pointing_at_two_different_students_refuse_the_row));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var one = db.SeedRegistration("Sara", "Bennani", academicYearId: TestHarness.PreviousYearId);
        var other = db.SeedRegistration("Ali", "Amrani", academicYearId: TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, one.Student!.CNE, "Sara", "Bennani", appogee: other.Student!.Appogee) };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.IdentifierConflict);
        preview.Value.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task The_same_student_twice_in_one_file_refuses_it()
    {
        await using var db = TestHarness.NewContext(nameof(The_same_student_twice_in_one_file_refuses_it));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "D1", "Sara", "Bennani", email: "a@um5.ac.ma"),
            Row(3, "D1", "Sara", "Bennani", email: "b@um5.ac.ma"),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Should().Contain(r => r.Action == InscriptionAction.DuplicateInFile);
        preview.Value.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task A_row_naming_nobody_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_row_naming_nobody_refuses_the_file));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, null!, "Sara", "Bennani") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.NoIdentifier);
    }

    [Fact]
    public async Task A_newcomer_without_a_name_cannot_be_created()
    {
        await using var db = TestHarness.NewContext(nameof(A_newcomer_without_a_name_cannot_be_created));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { new InscriptionRow(2, "X1", null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null) };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.MissingName);
    }

    [Fact]
    public async Task An_unreadable_date_is_reported_against_its_own_row()
    {
        await using var db = TestHarness.NewContext(nameof(An_unreadable_date_is_reported_against_its_own_row));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { new InscriptionRow(2, "X1", null, "Bennani", "Sara", null, "s@um5.ac.ma",
            "M", "le 3 mars", null, "2025", "SVT", null, null, null, null, null, null, null) };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.InvalidValue);
        preview.Value.CanApply.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ « Retrait » is a status the legacy base wore as a level, and it refuses the file rather than
    /// a line of it: nobody is inscribed into a marker with no stages and nothing to rotate.
    /// </summary>
    [Fact]
    public async Task A_level_that_is_not_a_promotion_refuses_the_whole_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_level_that_is_not_a_promotion_refuses_the_whole_file));
        SeedPromotions(db);
        db.SeedLevel(99, "Retrait", year: 0);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "X1", "Sara", "Bennani", email: "s@um5.ac.ma") };

        var preview = await PreviewHandler(db).Handle(new PreviewInscriptionQuery(rows, 99), default);

        preview.IsFailure.Should().BeTrue();
        preview.Error.Code.Should().Be("Inscription.NotAPromotion");
    }

    [Fact]
    public async Task One_bad_row_writes_nothing_at_all()
    {
        await using var db = TestHarness.NewContext(nameof(One_bad_row_writes_nothing_at_all));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "OK1", "Sara", "Bennani", email: "s@um5.ac.ma"),
            Row(3, null!, "Ali", "Amrani"),
        };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("Inscription.Rejected");
        (await db.Students.CountAsync()).Should().Be(0);
        (await db.Registrations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Only_the_administration_may_inscribe()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_administration_may_inscribe));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "X1", "Sara", "Bennani", email: "s@um5.ac.ma") };

        var handler = new ApplyInscriptionCommandHandler(
            Planner(db), Applier(db), db.StrangerAuthorizer());

        var applied = await handler.Handle(Apply(rows, confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("Inscription.NotAllowed");
        (await db.Students.CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // The final-year gate reaches this path too
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A guard the réinscription applies and this act does not is a guard anyone steps around by
    /// using the other button. It bites only on students PGSH already holds: a newcomer has no cursus
    /// here to owe anything from.
    /// </summary>
    [Fact]
    public async Task A_returner_owing_an_earlier_stage_cannot_re_enter_his_final_year()
    {
        await using var db = TestHarness.NewContext(nameof(A_returner_owing_an_earlier_stage_cannot_re_enter_his_final_year));
        var stage = db.SeedCatalog();
        var finalLevel = db.SeedLevel(50, "6ème année", year: 6);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var past = db.SeedRegistration("Reda", "Ghali", academicYearId: TestHarness.PreviousYearId);
        past.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);

        var group = db.SeedGroup(groupId: 60, groupNumber: 6);
        var cohort = db.SeedCohortFor(stage, group, cohortId: 70);
        var service = db.SeedService(1, "Service de Cardiologie");

        // Driven through the real lifecycle rather than by setting Result, which is derived: a mark
        // below 10 is what makes an attempt NonValidé, and only a failed attempt is owed.
        db.SeedGradedAssignment(past, cohort, service, mark: 7m);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, past.Student.CNE, "Reda", "Ghali") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, finalLevel.Id), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.FinalYearBlocked);
        preview.Value.CanApply.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // Identity: CNE and Apogée identify, CIN and e-mail corroborate
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ The dangerous one. An e-mail mistyped to somebody else's address used to make the row
    /// *match* that person — so a newcomer's line silently gave an existing student a registration
    /// under a different name, with nothing anywhere saying so. All four identifiers are unique in the
    /// store, but only the CNE and the Apogée are what a row is understood to name.
    /// </summary>
    [Fact]
    public async Task An_address_belonging_to_someone_else_refuses_the_row_rather_than_matching_him()
    {
        await using var db = TestHarness.NewContext(nameof(An_address_belonging_to_someone_else_refuses_the_row_rather_than_matching_him));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var other = db.SeedRegistration("Ali", "Amrani", academicYearId: TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        // A brand-new CNE, and an address that is already Ali's.
        var rows = new[] { Row(2, "BRANDNEW1", "Sara", "Bennani", email: other.Student!.Email) };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.IdentifierConflict);
        preview.Value.CanApply.Should().BeFalse();

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);
        applied.IsFailure.Should().BeTrue();

        // Neither created, nor quietly registered against Ali.
        (await db.Students.CountAsync()).Should().Be(1);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == TestHarness.CurrentYearId))
            .Should().Be(0);
    }

    /// <summary>
    /// ⚠ <c>IX_Registration_Student_Year</c> is unique, so one person on two lines is a raw constraint
    /// violation at SaveChanges — a 500 with nothing actionable in it. Keying the check on the first
    /// identifier alone missed the case where the two lines name him differently.
    /// </summary>
    [Fact]
    public async Task One_student_written_twice_under_different_identifiers_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(One_student_written_twice_under_different_identifiers_refuses_the_file));
        SeedPromotions(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var past = db.SeedRegistration("Hamza", "Tazi", academicYearId: TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, past.Student!.CNE, "Hamza", "Tazi"),
            Row(3, null!, "Hamza", "Tazi", appogee: past.Student.Appogee),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, TestHarness.LevelId), default);

        preview.Value.Rows.Should().Contain(r => r.Action == InscriptionAction.DuplicateInFile);
        preview.Value.CanApply.Should().BeFalse();
    }

    /// <summary>The same for two *new* people who share an identifier the store has never seen.</summary>
    [Fact]
    public async Task Two_new_rows_sharing_an_apogee_number_refuse_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(Two_new_rows_sharing_an_apogee_number_refuse_the_file));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[]
        {
            Row(2, "N1", "Sara", "Bennani", appogee: "AP777"),
            Row(3, "N2", "Ali", "Amrani", appogee: "AP777"),
        };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Should().Contain(r => r.Action == InscriptionAction.DuplicateInFile);
        preview.Value.CanApply.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // A manufactured identifier must be one the edit form can save
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ A validator describes what a <b>save</b> must satisfy. <c>SANS-CNE-</c> costs 9 of the 20
    /// characters <c>StudentIdentifierRules.CnePattern</c> allows, so a long Apogée would create a
    /// student whose file could never be saved again — the refusal naming a field nobody was editing.
    /// That is how 5 646 students became read-only once already.
    /// </summary>
    [Fact]
    public async Task A_provisional_cne_that_the_edit_form_could_not_save_is_refused_at_creation()
    {
        await using var db = TestHarness.NewContext(nameof(A_provisional_cne_that_the_edit_form_could_not_save_is_refused_at_creation));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, null!, "Ines", "Berrada", appogee: "AP-000000000000001") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        preview.Value.Rows.Single().Action.Should().Be(InscriptionAction.InvalidValue);
        preview.Value.Rows.Single().Message.Should().Contain("CNE");
        preview.Value.CanApply.Should().BeFalse();
    }

    /// <summary>The control: a real Apogée fits, and the provisional code is saveable.</summary>
    [Fact]
    public async Task A_provisional_cne_from_an_ordinary_apogee_number_is_a_valid_identifier()
    {
        await using var db = TestHarness.NewContext(nameof(A_provisional_cne_from_an_ordinary_apogee_number_is_a_valid_identifier));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, null!, "Ines", "Berrada", appogee: "10001373") };

        var applied = await ApplyHandler(db).Handle(Apply(rows, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var student = await db.Students.SingleAsync(s => s.Appogee == "10001373");
        student.CNE.Should().Be("SANS-CNE-10001373");
        StudentIdentifierRules.IsValidCne(student.CNE).Should().BeTrue();
    }

    /// <summary>
    /// ⚠ The two generators must agree. <c>LegacyIdentityMapper</c> manufactured all 10 204 imported
    /// addresses keeping <b>letters only</b>; a second copy here that kept digits too would give one
    /// faculty two address namespaces, and re-running the import would renumber people who already
    /// log in.
    /// </summary>
    [Fact]
    public async Task A_generated_address_follows_the_same_rule_as_the_legacy_import()
    {
        await using var db = TestHarness.NewContext(nameof(A_generated_address_follows_the_same_rule_as_the_legacy_import));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var rows = new[] { Row(2, "D1", "Mohamed2", "Al-Aoui") };

        var preview = await PreviewHandler(db).Handle(
            new PreviewInscriptionQuery(rows, FirstYearLevelId), default);

        string expected = StudentIdentifierRules.EmailCandidate(
            StudentIdentifierRules.EmailLocalPart("Mohamed2", "Al-Aoui"), 0);

        preview.Value.Rows.Single().GeneratedEmail.Should().Be(expected);
        expected.Should().Be("mohamed_alaoui@um5.ac.ma");
    }

    // ---------------------------------------------------------------------------------------------
    // One student at a time — the escape hatch every bulk import owes
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The November transfer. Re-sending the September file to add one person would mean re-stating a
    /// whole promotion to say one thing.
    /// </summary>
    [Fact]
    public async Task One_student_can_be_inscribed_without_a_file()
    {
        await using var db = TestHarness.NewContext(nameof(One_student_can_be_inscribed_without_a_file));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var command = new InscribeStudentCommand(
            TestHarness.LevelId, "T99", null, "Alaoui", "Omar",
            OriginInstitution: "FMP Casablanca",
            OriginLastYearCompleted: "2",
            EquivalenceReference: "Arrêté 12/2026");

        var result = await SingleHandler(db).Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be(InscriptionAction.TransferIn);
        result.Value.CreatesStudent.Should().BeTrue();
        result.Value.RecordsOrigin.Should().BeTrue();

        (await db.Students.CountAsync(s => s.CNE == "T99")).Should().Be(1);
        (await db.PriorEnrolments.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// ⚠ The refusal has to name the field. « 1 ligne en erreur » is what a file needs and explains
    /// nothing to somebody who typed one person in, so the single path returns the row's own sentence
    /// and the action as the code.
    /// </summary>
    [Fact]
    public async Task A_single_inscription_is_refused_in_the_rows_own_words()
    {
        await using var db = TestHarness.NewContext(nameof(A_single_inscription_is_refused_in_the_rows_own_words));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var command = new InscribeStudentCommand(TestHarness.LevelId, "T99", null, "Alaoui", "Omar");

        var result = await SingleHandler(db).Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"Inscription.{InscriptionAction.OriginRequired}");
        result.Error.Description.Should().Contain("équivalence");
        (await db.Students.CountAsync()).Should().Be(0);
    }

    /// <summary>The single path is no laxer than the file: same planner, same guards.</summary>
    [Fact]
    public async Task A_single_inscription_of_someone_already_registered_creates_nothing()
    {
        await using var db = TestHarness.NewContext(nameof(A_single_inscription_of_someone_already_registered_creates_nothing));
        SeedPromotions(db);
        var existing = db.SeedRegistration("Karim", "Fassi");
        await db.SaveChangesAsync();

        var command = new InscribeStudentCommand(
            TestHarness.LevelId, existing.Student!.CNE, null, "Fassi", "Karim");

        var result = await SingleHandler(db).Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be(InscriptionAction.AlreadyRegistered);
        (await db.Registrations.CountAsync(r => r.StudentId == existing.StudentId)).Should().Be(1);
    }

    [Fact]
    public async Task Only_the_administration_may_inscribe_one_student()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_administration_may_inscribe_one_student));
        SeedPromotions(db);
        await db.SaveChangesAsync();

        var handler = new InscribeStudentCommandHandler(
            Planner(db), Applier(db), db.StrangerAuthorizer());

        var result = await handler.Handle(
            new InscribeStudentCommand(FirstYearLevelId, "X1", null, "Bennani", "Sara"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Inscription.NotAllowed");
        (await db.Students.CountAsync()).Should().Be(0);
    }
}
