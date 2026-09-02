using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Progression;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The year rollover driven by the faculty's own réinscription roll: one line per student, stating
/// the étape he was in and the étape he enters. The verdict on the closing year is carried by that
/// movement, so one upload closes a year and opens the next.
///
/// <para>The cases that matter most are the ones where a level that has <em>not</em> moved means
/// something other than a redoublement — a final year whose thesis is not defended, a réorientation,
/// a student with no registration to pronounce on. Each of those writes a registration and no
/// verdict, and getting the first of them wrong would have annulled a year of stage record for 804
/// students of the real 2026-2027 file.</para>
/// </summary>
public class ReinscriptionSheetTests
{
    private const int NextYearId = 3;
    private const int Med4LevelId = 4;
    private const int Med6LevelId = 6;
    private const int Med7LevelId = 7;
    private const int RetraitLevelId = 90;
    private const int Pharma1LevelId = 51;

    // ---------------------------------------------------------------------------------------------
    // Fixture
    // ---------------------------------------------------------------------------------------------

    private static ReinscriptionSheetPlanner Planner(ApplicationDbContext db) =>
        new(db, new FinalYearGuard(db, new OutstandingStageFinder(db)));

    private static ApplyReinscriptionSheetCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, Planner(db), new RegistrationCnpnStamper(db, new CnpnAssignment(db)), db.AdminAuthorizer());

    private static PreviewReinscriptionSheetQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(Planner(db), db.AdminAuthorizer());

    /// <summary>
    /// <c>SeedCatalog</c>'s 3ᵉ année Médecine, the levels the roll can send it to, « Retrait », a
    /// Pharmacie level for the réorientation case, and the year that receives them all.
    /// </summary>
    private static void SeedCatalogue(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedLevel(Med4LevelId, "4ème année", year: 4);
        db.SeedLevel(Med6LevelId, "6ème année", year: 6);
        db.SeedLevel(Med7LevelId, "7ème année", year: 7);
        db.SeedLevel(RetraitLevelId, "Retrait", year: 0);
        db.SeedLevel(Pharma1LevelId, "1ère année Pharmacie", year: 1, program: AcademicProgram.Pharmacie);
        db.SeedAcademicYear(NextYearId, "2026-2027",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));
    }

    /// <summary>A student of the closing year carrying the numéro Apogée the roll will name him by.</summary>
    private static Registration Enrolled(
        ApplicationDbContext db, string code, int levelId = TestHarness.LevelId, int? cnpnVersionId = null)
    {
        var registration = db.SeedRegistration("Jean", $"Etudiant{code}", levelId: levelId);
        registration.Student.Appogee = code;

        // The registration's own text first, which is the order every CNPN read uses. Through the
        // aggregate, never the setter — it is the only writer, and the freeze rule lives there.
        if (cnpnVersionId is { } versionId)
            registration.StampCnpnVersion(versionId, RegistrationCnpnSource.StudentStamp)
                .IsSuccess.Should().BeTrue();

        return registration;
    }

    private static ReinscriptionSheetRow Row(int sheetRow, string code, string from, string to) =>
        new(sheetRow, code, "Etudiant", "Jean", from, to);

    /// <summary>
    /// The apply, confirming <paramref name="graduations"/> graduations. Defaults to 0 — most cases
    /// here seed nobody in a final year, and a case that does says so explicitly, which is the point
    /// of the guard.
    /// </summary>
    private static Task<SharedKernel.Result<ReinscriptionSheetReport>> Apply(
        ApplicationDbContext db, params ReinscriptionSheetRow[] rows) =>
        ApplyConfirming(db, 0, rows);

    private static Task<SharedKernel.Result<ReinscriptionSheetReport>> ApplyConfirming(
        ApplicationDbContext db, int graduations, params ReinscriptionSheetRow[] rows) =>
        ApplyHandler(db).Handle(
            new ApplyReinscriptionSheetCommand(
                rows, TestHarness.CurrentYearId, NextYearId, graduations),
            default);

    // ---------------------------------------------------------------------------------------------
    // The happy path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_level_that_moves_up_records_admis_and_creates_the_next_registration()
    {
        await using var db = TestHarness.NewContext(nameof(A_level_that_moves_up_records_admis_and_creates_the_next_registration));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegister.Should().Be(1);
        result.Value.WillRecordOutcome.Should().Be(1);

        source.Status.Should().Be(RegistrationStatus.Validated);
        source.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared,
            "the file is the faculty's own statement, not PGSH reading an enrolment sequence");

        var created = await db.Registrations
            .SingleAsync(r => r.AcademicYearId == NextYearId && r.StudentId == source.StudentId);

        created.LevelId.Should().Be(Med4LevelId);
        created.Status.Should().Be(RegistrationStatus.Active);
        created.AcademicGroupId.Should().BeNull("répartition is a later act");
    }

    /// <summary>
    /// The renamed code and the original one are the same level, so a 3ᵉ année student repeating it
    /// under the new vocabulary is a redoublement and not a move.
    /// </summary>
    [Fact]
    public async Task A_level_that_does_not_move_records_redoublant_below_the_final_year()
    {
        await using var db = TestHarness.NewContext(nameof(A_level_that_does_not_move_records_redoublant_below_the_final_year));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MDME3"));

        result.IsSuccess.Should().BeTrue();
        source.Status.Should().Be(RegistrationStatus.Failed);

        var created = await db.Registrations
            .SingleAsync(r => r.AcademicYearId == NextYearId && r.StudentId == source.StudentId);

        created.LevelId.Should().Be(TestHarness.LevelId, "a redoublant repeats the level he was in");
    }

    // ---------------------------------------------------------------------------------------------
    // Where a level that has not moved is not a redoublement
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ The case the whole act turns on. 804 lines of the real 2026-2027 file are final-year students
    /// re-registering in the same year — 659 in 7ᵉ année Médecine, 145 in 6ᵉ année Pharmacie — because
    /// the thesis year runs until the thesis is defended and PGSH holds no record of a defence.
    /// Recording <c>Failed</c> there would be wrong twice: it is not a failure, and
    /// <c>RegistrationStatus.AnnulsItsStages</c> would wipe the year's stage record.
    /// </summary>
    [Fact]
    public async Task A_final_year_repeat_is_registered_with_no_verdict_at_all()
    {
        await using var db = TestHarness.NewContext(nameof(A_final_year_repeat_is_registered_with_no_verdict_at_all));
        SeedCatalogue(db);

        // The seven-year text, so the 7ᵉ année really is his last one.
        var source = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();
        var statusBefore = source.Status;

        var result = await Apply(db, Row(2, "13014449", "MED07", "MED07"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegister.Should().Be(1);
        result.Value.WillRecordOutcome.Should().Be(0);

        source.Status.Should().Be(statusBefore, "nothing was pronounced on the closing year");
        source.Status.Should().NotBe(RegistrationStatus.Failed,
            "Failed annuls the year's stages, and a thesis year is not a redoublement");
        source.OutcomeSource.Should().BeNull();

        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId
                                           && r.StudentId == source.StudentId
                                           && r.LevelId == Med7LevelId))
            .Should().BeTrue("he is re-registered even though no verdict is recorded");
    }

    /// <summary>
    /// A student nobody has stamped falls back on the shortest text of his programme, and the
    /// fallback errs towards « peut-être » — which here means « write nothing », the safe direction.
    /// </summary>
    [Fact]
    public async Task An_unstamped_student_in_a_possible_final_year_gets_no_verdict_either()
    {
        await using var db = TestHarness.NewContext(nameof(An_unstamped_student_in_a_possible_final_year_gets_no_verdict_either));
        SeedCatalogue(db);
        var source = Enrolled(db, "13000088", Med6LevelId);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "13000088", "MED06", "MED06"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRecordOutcome.Should().Be(0);
        source.OutcomeSource.Should().BeNull();
    }

    /// <summary>
    /// A réorientation compares nothing: a 3ᵉ année Médecine against a 1ʳᵉ année Pharmacie is not a
    /// year lost, and the file does not claim it is.
    /// </summary>
    [Fact]
    public async Task A_programme_change_registers_the_student_without_pronouncing_on_the_year()
    {
        await using var db = TestHarness.NewContext(nameof(A_programme_change_registers_the_student_without_pronouncing_on_the_year));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MPHAR1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegister.Should().Be(1);
        result.Value.WillRecordOutcome.Should().Be(0);
        source.OutcomeSource.Should().BeNull();

        var created = await db.Registrations
            .SingleAsync(r => r.AcademicYearId == NextYearId && r.StudentId == source.StudentId);

        created.LevelId.Should().Be(Pharma1LevelId);
    }

    /// <summary>
    /// A student the closing year holds no registration for — a returner after an interrupted year.
    /// Three lines of the real file are this. The file's word for where he goes stands; its word for
    /// where he was cannot be checked, so nothing is pronounced.
    /// </summary>
    [Fact]
    public async Task A_student_with_no_registration_in_the_closing_year_is_registered_and_flagged()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_with_no_registration_in_the_closing_year_is_registered_and_flagged));
        SeedCatalogue(db);

        // Registered two years ago, absent from the closing year.
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var old = Enrolled(db, "20013007");
        old.AcademicYearId = TestHarness.PreviousYearId;
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "20013007", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WithoutSourceRegistration.Should().Be(1);
        result.Value.WillRecordOutcome.Should().Be(0);

        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId
                                           && r.StudentId == old.StudentId))
            .Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // Skips — reported, never blocking
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The masters. 23 lines of the real file name a programme PGSH holds no level, no stage and no
    /// CNPN for; reading them as « code inconnu » would refuse the other 6 839 over rows that are not
    /// mistakes.
    /// </summary>
    [Fact]
    public async Task A_programme_outside_the_scope_is_skipped_and_the_rest_of_the_file_applies()
    {
        await using var db = TestHarness.NewContext(nameof(A_programme_outside_the_scope_is_skipped_and_the_rest_of_the_file_applies));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await Apply(db,
            Row(2, "25030191", "MMBTM1", "MMBTM2"),
            Row(3, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.OutsideScope.Should().Be(1);
        result.Value.WillRegister.Should().Be(1);
        source.Status.Should().Be(RegistrationStatus.Validated);
    }

    /// <summary>
    /// ⚠ <b>The roll creates the students it names and PGSH has never seen.</b> Skipping them was
    /// defensible — creating an identity is the inscription's act — and it was still wrong in
    /// practice: the 26 such rows of the real file ended up in a downloaded spreadsheet and nowhere
    /// anybody works, so nobody acted on them. They are created from what the file carries and
    /// flagged so the dossier gets finished.
    /// </summary>
    [Fact]
    public async Task An_unknown_code_creates_the_student_and_flags_the_thin_dossier()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_code_creates_the_student_and_flags_the_thin_dossier));
        SeedCatalogue(db);
        Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await Apply(db,
            new ReinscriptionSheetRow(2, "99999999", "BOLOKI", "Ismail", "MED03", "MED04"),
            Row(3, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedStudents.Should().Be(1);
        result.Value.WillRegister.Should().Be(2, "the created student is registered like the other");
        result.Value.GeneratedEmails.Should().Be(1);

        var created = await db.Users.OfType<Student>().SingleAsync(u => u.Appogee == "99999999");
        created.LastName.Should().Be("BOLOKI");
        created.FirstName.Should().Be("Ismail");
        created.AcademicProgram.Should().Be(AcademicProgram.Medecine, "the programme is the level's");

        // ⚠ No CNE is manufactured: the row carries an Apogée and Student.CNE is optional, so a
        // SANS-CNE- placeholder would read in every list exactly like a code somebody holds.
        created.CNE.Should().BeNull();
        created.Email.Should().Be("ismail_boloki@um5.ac.ma");

        var registration = await db.Registrations
            .Include(r => r.Holds)
            .SingleAsync(r => r.StudentId == created.Id && r.AcademicYearId == NextYearId);

        registration.LevelId.Should().Be(Med4LevelId);
        registration.Holds.Single().Reason.Should().Be(RegistrationHoldReason.IncompleteStudentFile);
    }

    /// <summary>
    /// ⚠ <b>The flag on a created student is advisory, and that is the whole point of it.</b> His
    /// dossier is thin, not wrong — nothing about a missing date de naissance says he may not rotate
    /// through a service — so he is cut into a roster and planned like anyone else while somebody
    /// finishes his file. Freezing him would be treating a missing birth date like an unexplained
    /// absence.
    /// </summary>
    [Fact]
    public async Task A_created_student_is_flagged_but_still_planned()
    {
        await using var db = TestHarness.NewContext(nameof(A_created_student_is_flagged_but_still_planned));
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        await Apply(db, new ReinscriptionSheetRow(2, "99999999", "BOLOKI", "Ismail", "MED03", "MED04"));

        var registration = await db.Registrations
            .Include(r => r.Holds)
            .SingleAsync(r => r.AcademicYearId == NextYearId);

        registration.IsFlagged.Should().BeTrue("it is on the worklist");
        registration.IsOnHold.Should().BeFalse("but nothing about it blocks planning");
        RegistrationHoldPolicy.IsPlannable(registration).Should().BeTrue();
    }

    /// <summary>
    /// Two unmatched lines whose names collide must not be handed the same address: an e-mail is a
    /// login, and <c>Users.Email</c> is NOT NULL UNIQUE.
    /// </summary>
    [Fact]
    public async Task Two_created_students_with_the_same_name_get_different_addresses()
    {
        await using var db = TestHarness.NewContext(nameof(Two_created_students_with_the_same_name_get_different_addresses));
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        await Apply(db,
            new ReinscriptionSheetRow(2, "99999991", "BOLOKI", "Ismail", "MED03", "MED04"),
            new ReinscriptionSheetRow(3, "99999992", "BOLOKI", "Ismail", "MED03", "MED04"));

        var mails = await db.Users.OfType<Student>()
            .Where(u => u.Appogee == "99999991" || u.Appogee == "99999992")
            .Select(u => u.Email)
            .ToListAsync();

        mails.Should().HaveCount(2);
        mails.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// ⚠ And the collision is checked against the <b>store</b>, not merely against the batch: an
    /// address already held by somebody is a Keycloak login, and handing it out again gives a student
    /// another person's account.
    /// </summary>
    [Fact]
    public async Task A_created_student_never_takes_an_address_already_in_the_base()
    {
        await using var db = TestHarness.NewContext(nameof(A_created_student_never_takes_an_address_already_in_the_base));
        SeedCatalogue(db);

        var sitting = db.SeedRegistration("Ismail", "Boloki");
        sitting.Student.Email = "ismail_boloki@um5.ac.ma";
        await db.SaveChangesAsync();

        await Apply(db, new ReinscriptionSheetRow(2, "99999999", "BOLOKI", "Ismail", "MED03", "MED04"));

        var created = await db.Users.OfType<Student>().SingleAsync(u => u.Appogee == "99999999");
        created.Email.Should().NotBe("ismail_boloki@um5.ac.ma");
        created.Email.Should().Be("ismail_boloki2@um5.ac.ma");
    }

    /// <summary>
    /// ⚠ <b>A re-run must not turn the students it already rolled over into absentees.</b> Found on
    /// the live base 2026-09-02: the second upload of the same file offered <b>8 077</b> gels and
    /// <b>791</b> « Diplômé » déduits, where the first pass had found 1 267 and 1 217 — because a row
    /// skipped as « déjà inscrit » dropped its source registration, so <c>ReadAbsence</c> stopped
    /// seeing that student as covered by the file and inferred a soutenance from his silence.
    ///
    /// <para>He is named on his own line. « Couvert » means the file mentions him, not that the line
    /// produced a write.</para>
    /// </summary>
    [Fact]
    public async Task Re_running_does_not_turn_already_registered_students_into_absentees()
    {
        await using var db = TestHarness.NewContext(nameof(Re_running_does_not_turn_already_registered_students_into_absentees));
        SeedCatalogue(db);

        // A final-year student, so a wrongly-inferred absence would graduate him — the damaging case.
        var finalYear = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        var first = await Apply(db, Row(2, "13014449", "MED07", "MED07"));
        first.IsSuccess.Should().BeTrue();
        first.Value.NotCovered.Should().Be(0, "the file names the only registration there is");

        var second = await Apply(db, Row(2, "13014449", "MED07", "MED07"));

        second.IsSuccess.Should().BeTrue();
        second.Value.AlreadyRegistered.Should().Be(1);
        second.Value.NotCovered.Should().Be(0, "he is named on his own line, not absent from it");
        second.Value.WillGraduate.Should().Be(0);
        second.Value.AbsenteesHeld.Should().Be(0);

        finalYear.Status.Should().NotBe(RegistrationStatus.Graduated,
            "a re-run must not end the cursus of a student the file re-registers");

        (await db.RegistrationHolds.CountAsync()).Should().Be(0);
    }

    /// <summary>The apply is re-runnable: a student already rolled over is left exactly as he is.</summary>
    [Fact]
    public async Task Re_running_the_same_file_creates_nothing_further()
    {
        await using var db = TestHarness.NewContext(nameof(Re_running_the_same_file_creates_nothing_further));
        SeedCatalogue(db);
        Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        (await Apply(db, Row(2, "24008386", "MED03", "MED04"))).IsSuccess.Should().BeTrue();
        var second = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        second.IsSuccess.Should().BeTrue();
        second.Value.AlreadyRegistered.Should().Be(1);
        second.Value.WillRegister.Should().Be(0);

        (await db.Registrations.CountAsync(r => r.AcademicYearId == NextYearId)).Should().Be(1);
    }

    /// <summary>
    /// ⚠ <b>The faculty's roll outranks our stage record, and the disagreement is recorded rather
    /// than acted on.</b> A student the file sends into the last year of his own text while an
    /// earlier stage still reads unvalidated <em>is</em> re-registered — refusing him used to drop
    /// 182 of the 651 7ᵉ année Médecine the faculty itself named as coming back, and in most of those
    /// cases the stage was served and only the évaluation is missing, which is a fact about our data
    /// entry rather than about the student.
    ///
    /// <para>What replaces the refusal is a hold: the registration exists, and
    /// <c>RegistrationHoldReason.OutstandingPriorStages</c> keeps it out of every roster and every
    /// affectation until scolarité clears it. He may not start his final year's stages before the
    /// earlier ones are settled — which is what the hold says and what a skip could not.</para>
    /// </summary>
    [Fact]
    public async Task A_student_owing_an_earlier_stage_is_registered_into_his_final_year_and_held()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_owing_an_earlier_stage_is_registered_into_his_final_year_and_held));
        SeedCatalogue(db);

        // The four-year text, so the 4ᵉ année he is entering is his last one, and a failed 3ᵉ année
        // stage below it is a debt.
        db.CnpnVersions.Local.First(v => v.Id == TestHarness.NewCnpnId)
            .Correct("1650.25", "CNPN 1650.25", totalYears: 4, reference: null,
                     appliesToEntrantsFromAcademicYearId: TestHarness.CurrentYearId, CnpnSpanFloor.None)
            .IsSuccess.Should().BeTrue();

        var source = Enrolled(db, "13000045", cnpnVersionId: TestHarness.NewCnpnId);
        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var cohort = db.SeedCohort(stage, groupId: 900, groupLabel: "G900");
        var service = db.SeedService(1, "Service de Cardiologie");
        db.SeedGradedAssignment(source, cohort, service, mark: 7);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "13000045", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegisterHeld.Should().Be(1);
        result.Value.WillRegister.Should().Be(1, "the faculty named him; the registration is created");

        var created = await db.Registrations
            .Include(r => r.Holds)
            .SingleAsync(r => r.AcademicYearId == NextYearId);

        created.LevelId.Should().Be(Med4LevelId);
        created.IsOnHold.Should().BeTrue();

        var hold = created.Holds.Single();
        hold.Reason.Should().Be(RegistrationHoldReason.OutstandingPriorStages);
        hold.ReleasedOn.Should().BeNull();
        hold.Evidence.Should().Contain("stage",
            "the hold carries the guard's own sentence, so the operator reads what was actually seen");
    }

    /// <summary>
    /// The other half of the same act: a held registration takes no part in the répartition. Without
    /// this the hold is a label, and « gelé » would mean nothing at all.
    /// </summary>
    [Fact]
    public async Task A_held_registration_is_left_out_of_the_roster_cut()
    {
        await using var db = TestHarness.NewContext(nameof(A_held_registration_is_left_out_of_the_roster_cut));
        db.SeedCatalog();

        var free = db.SeedRegistration("Amine", "Libre");
        var frozen = db.SeedRegistration("Salma", "Gelee");

        frozen.PlaceOnHold(
            RegistrationHoldReason.AbsentFromReinscriptionRoll,
            "Absente du fichier de réinscription 2026-2027.",
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await db.SaveChangesAsync();

        var handler = new AutoArrangeGroupsCommandHandler(db);

        var result = await handler.Handle(
            new AutoArrangeGroupsCommand(TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SuccessCount.Should().Be(1, "only the unheld student is cut into a roster");
        result.Value.FailureCount.Should().Be(1);

        (await db.Registrations.SingleAsync(r => r.Id == free.Id)).AcademicGroupId.Should().NotBeNull();
        (await db.Registrations.SingleAsync(r => r.Id == frozen.Id)).AcademicGroupId.Should().BeNull();

        // ⚠ Named, not merely dropped. A cut that is silently one student short looks exactly like a
        // promotion that size, which is the failure the whole mechanism exists to remove.
        var refused = result.Value.Items.Single(i => !i.IsSuccess);
        refused.Identifier.Should().Be(frozen.StudentId);
        refused.Error!.Code.Should().Be("Registrations.OnHold");
        refused.Error.Description.Should().Contain("réinscription");
    }

    /// <summary>
    /// ⚠ <b>The case the faculty's own process turns on, and the one that was wrong.</b> A final-year
    /// student re-registering into the same year is not beginning it — he is continuing, and the
    /// re-registration is precisely how he gets to revalidate the stages he still owes. The gate must
    /// stand aside, or it refuses him the only path out of the debt.
    ///
    /// <para>Measured on the real 2026-2027 roll: <b>182 of the 651</b> 7ᵉ année Médecine it
    /// re-registers were refused before this — a quarter of the promotion, every one named by the
    /// faculty as coming back. The « Réinscrits » figure read 616 instead of 798.</para>
    /// </summary>
    [Fact]
    public async Task A_final_year_student_owing_a_stage_is_still_re_registered_into_the_same_year()
    {
        await using var db = TestHarness.NewContext(nameof(A_final_year_student_owing_a_stage_is_still_re_registered_into_the_same_year));
        SeedCatalogue(db);

        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        // ⚠ The debt hangs off an EARLIER registration, because OutstandingStageFinder dates a debt
        // by the registration's level and not by the stage's. A failed attempt on the final-year
        // registration itself is not an earlier debt, and the gate rightly ignores it.
        var earlier = db.SeedRegistration("Jean", "Etudiant13014449",
            academicYearId: TestHarness.PreviousYearId);
        earlier.Student.Appogee = "13014449";
        earlier.Student.AssignCnpnVersion(TestHarness.OldCnpnId, isInferred: false)
            .IsSuccess.Should().BeTrue();
        earlier.StampCnpnVersion(TestHarness.OldCnpnId, RegistrationCnpnSource.StudentStamp)
            .IsSuccess.Should().BeTrue();

        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var cohort = db.SeedCohort(stage, groupId: 901, groupLabel: "G901");
        var service = db.SeedService(2, "Service de Chirurgie");
        db.SeedGradedAssignment(earlier, cohort, service, mark: 7);

        // …and he is currently in the 7ᵉ année, his last, which the roll re-registers him into.
        var source = new Registration
        {
            Id = Guid.NewGuid(),
            StudentId = earlier.StudentId,
            AcademicYearId = TestHarness.CurrentYearId,
            LevelId = Med7LevelId,
        };
        source.StampCnpnVersion(TestHarness.OldCnpnId, RegistrationCnpnSource.StudentStamp)
            .IsSuccess.Should().BeTrue();
        db.Registrations.Add(source);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "13014449", "MED07", "MED07"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegisterHeld.Should().Be(0,
            "he is continuing his final year, and the re-registration is how he revalidates");
        result.Value.WillRegister.Should().Be(1);
        result.Value.WillRecordOutcome.Should().Be(0, "and still no verdict — it is not a redoublement");

        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId
                                           && r.StudentId == source.StudentId
                                           && r.LevelId == Med7LevelId))
            .Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // What the file does not say: graduation from an absence
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ The roll is the list of who <em>is</em> coming back, so an absence in a student's last year
    /// is a defence. Measured on the real 2026-2027 file: 1 006 of the 1 657 in 7ᵉ année Médecine and
    /// 212 of the 356 in 6ᵉ année Pharmacie.
    /// </summary>
    [Fact]
    public async Task An_absentee_in_his_final_year_is_recorded_diplome()
    {
        await using var db = TestHarness.NewContext(nameof(An_absentee_in_his_final_year_is_recorded_diplome));
        SeedCatalogue(db);

        var listed = Enrolled(db, "24008386");
        // Seven-year text, sitting in the 7ᵉ année: this is exactly his last year.
        var absentee = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        var result = await ApplyConfirming(db, 1, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillGraduate.Should().Be(1);
        result.Value.NotCovered.Should().Be(1);

        absentee.Status.Should().Be(RegistrationStatus.Graduated);
        absentee.OutcomeSource.Should().Be(RegistrationOutcomeSource.Inferred,
            "PGSH read an absence — nobody named him on a document, and a later defence roll "
            + "(Declared) must be able to overwrite this");

        listed.Status.Should().Be(RegistrationStatus.Validated, "the listed row is unaffected");
    }

    /// <summary>
    /// ⚠ <b>Every absentee is held, the graduations included — and that is the point of contention
    /// worth stating.</b> The graduation is <em>PGSH's inference</em>, read off a blank cell, not the
    /// faculty's statement. A partial roll would therefore end the cursus of people still enrolled,
    /// with nothing on the row saying a human had ever looked at it. Holding costs a genuine graduate
    /// nothing — his year is closed and there is no next one to plan — and catches the case an
    /// absence most often really is: a réinscription that has not arrived, where the hold is still
    /// standing on the day somebody registers him by hand.
    ///
    /// <para>The listed student, by contrast, is not held: the file names him, and nothing about him
    /// is in doubt.</para>
    /// </summary>
    [Fact]
    public async Task Every_absentee_is_held_including_the_ones_recorded_diplome()
    {
        await using var db = TestHarness.NewContext(nameof(Every_absentee_is_held_including_the_ones_recorded_diplome));
        SeedCatalogue(db);

        var listed = Enrolled(db, "24008386");
        var graduating = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        // Absent in the 4ᵉ année of a seven-year text: not a fin de cursus, so nothing is written —
        // and this is the row that most obviously needs a human.
        var undecidable = Enrolled(db, "13099999", Med4LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        var result = await ApplyConfirming(db, 1, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.NotCovered.Should().Be(2);
        result.Value.WillGraduate.Should().Be(1);
        result.Value.AbsentNeedingAttention.Should().Be(1);
        result.Value.AbsenteesHeld.Should().Be(2, "every absentee is held, not only the undecided one");

        var held = await db.Registrations
            .Include(r => r.Holds)
            .Where(r => r.AcademicYearId == TestHarness.CurrentYearId)
            .ToListAsync();

        held.Single(r => r.Id == graduating.Id).IsOnHold.Should().BeTrue();
        held.Single(r => r.Id == undecidable.Id).IsOnHold.Should().BeTrue();
        held.Single(r => r.Id == listed.Id).IsOnHold.Should().BeFalse("the file names him");

        graduating.Holds.Single().Reason
            .Should().Be(RegistrationHoldReason.AbsentFromReinscriptionRoll);

        // The verdict is still recorded — the hold sits on top of it, it does not replace it.
        graduating.Status.Should().Be(RegistrationStatus.Graduated);
        graduating.OutcomeSource.Should().Be(RegistrationOutcomeSource.Inferred);
    }

    /// <summary>
    /// The roll is re-runnable by design, and that has to survive the holds: a second upload of the
    /// same file must not stack a second flag on every absentee, nor rewrite evidence somebody is in
    /// the middle of acting on.
    /// </summary>
    [Fact]
    public async Task Re_running_the_roll_does_not_stack_holds()
    {
        await using var db = TestHarness.NewContext(nameof(Re_running_the_roll_does_not_stack_holds));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        var absentee = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        await ApplyConfirming(db, 1, Row(2, "24008386", "MED03", "MED04"));
        var second = await ApplyConfirming(db, 0, Row(2, "24008386", "MED03", "MED04"));

        second.IsSuccess.Should().BeTrue();
        absentee.Holds.Should().ContainSingle();
    }

    /// <summary>
    /// ⚠ An absence is only decidable at the <em>end</em> of a cursus. Below one it could be an
    /// abandon, an exclusion, or a réinscription that has not arrived — 47 such rows on the real
    /// file — and nothing in the document distinguishes them.
    /// </summary>
    [Fact]
    public async Task An_absentee_below_his_final_year_is_left_alone_and_named()
    {
        await using var db = TestHarness.NewContext(nameof(An_absentee_below_his_final_year_is_left_alone_and_named));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        var absentee = Enrolled(db, "24008387", cnpnVersionId: TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillGraduate.Should().Be(0);
        result.Value.AbsentNeedingAttention.Should().Be(1);

        absentee.OutcomeSource.Should().BeNull();
        result.Value.Absentees.Single().Outcome
            .Should().Be(ReinscriptionSheetAbsenceOutcome.BelowFinalYear);
    }

    /// <summary>
    /// ⚠ <b>Where this parts company with the déliberation, and why.</b> That canvas stands aside
    /// without a CNPN and lets « Diplômé » through, because the faculty <em>named</em> the student.
    /// An absence names nobody, so a student PGSH holds no text for is reported, never graduated on
    /// the programme's shortest text.
    /// </summary>
    [Fact]
    public async Task An_absentee_with_no_text_on_record_is_never_graduated()
    {
        await using var db = TestHarness.NewContext(nameof(An_absentee_with_no_text_on_record_is_never_graduated));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        var unstamped = Enrolled(db, "13014449", Med7LevelId);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillGraduate.Should().Be(0);
        unstamped.OutcomeSource.Should().BeNull();
        result.Value.Absentees.Single().Outcome
            .Should().Be(ReinscriptionSheetAbsenceOutcome.NoTextOnRecord);
    }

    /// <summary>
    /// ⚠ A registration sitting <em>above</em> its text's span is a data question, not a verdict —
    /// the base holds 6. <c>IsExactlyFinal</c> compares with <c>==</c> for exactly this, and the
    /// déliberation refuses the same row by name (<c>NotAFinalYear</c>).
    /// </summary>
    [Fact]
    public async Task An_absentee_above_his_texts_span_is_not_graduated()
    {
        await using var db = TestHarness.NewContext(nameof(An_absentee_above_his_texts_span_is_not_graduated));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        // 7ᵉ année carrying the six-year text: 7 > 6, so this is not the end of anything readable.
        var oddity = Enrolled(db, "13014449", Med7LevelId, TestHarness.NewCnpnId);
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.Value.WillGraduate.Should().Be(0);
        oddity.OutcomeSource.Should().BeNull();
        result.Value.Absentees.Single().Outcome
            .Should().Be(ReinscriptionSheetAbsenceOutcome.BelowFinalYear);
    }

    /// <summary>A verdict already recorded is never replaced by one derived from an absence.</summary>
    [Fact]
    public async Task An_absentee_already_carrying_a_verdict_is_untouched()
    {
        await using var db = TestHarness.NewContext(nameof(An_absentee_already_carrying_a_verdict_is_untouched));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        var decided = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        decided.RecordYearOutcome(
            RegistrationStatus.Excluded, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.WillGraduate.Should().Be(0);
        result.Value.AbsentAlreadyDecided.Should().Be(1);
        decided.Status.Should().Be(RegistrationStatus.Excluded);
    }

    /// <summary>
    /// ⚠ The confirmation is a <em>number</em>, and it is the only write of this act that needs one:
    /// a graduation lands on a student the file does not name, so a registration created between the
    /// simulation and the apply would have its cursus ended by a confirmation nobody gave for it.
    /// </summary>
    [Fact]
    public async Task A_graduation_count_that_does_not_match_refuses_the_whole_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_graduation_count_that_does_not_match_refuses_the_whole_file));
        SeedCatalogue(db);

        var listed = Enrolled(db, "24008386");
        var absentee = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        // The operator was shown 0 — a second final-year student appeared since.
        var result = await ApplyConfirming(db, 0, Row(2, "24008386", "MED03", "MED04"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReinscriptionSheet.GraduationsNotConfirmed");

        // ⚠ The assertion that matters: refused *before* the write, so neither half landed.
        absentee.OutcomeSource.Should().BeNull();
        listed.OutcomeSource.Should().BeNull();
        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId)).Should().BeFalse();
    }

    /// <summary>The dry run counts the graduations without recording one.</summary>
    [Fact]
    public async Task The_preview_counts_the_graduations_and_writes_none()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_counts_the_graduations_and_writes_none));
        SeedCatalogue(db);

        Enrolled(db, "24008386");
        var absentee = Enrolled(db, "13014449", Med7LevelId, TestHarness.OldCnpnId);
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(
            new PreviewReinscriptionSheetQuery(
                [Row(2, "24008386", "MED03", "MED04")], TestHarness.CurrentYearId, NextYearId),
            default);

        preview.Value.WillGraduate.Should().Be(1);
        absentee.OutcomeSource.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------
    // Refusals — and the store must be untouched after each
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ Every refusal asserts the store as well as the <c>Result</c>. A guard placed <em>after</em>
    /// the write returns the same failure and passes a handler test; only the store tells them apart.
    /// The second line is the control: it would have applied cleanly on its own.
    /// </summary>
    private static async Task RefusesWholeFile(
        ApplicationDbContext db, Registration control, params ReinscriptionSheetRow[] rows)
    {
        var result = await Apply(db, rows);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReinscriptionSheet.RowsRefused");

        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId))
            .Should().BeFalse("nothing is written when the file is refused");
        control.OutcomeSource.Should().BeNull("not even the lines that were fine");
    }

    [Fact]
    public async Task A_code_appearing_twice_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_code_appearing_twice_refuses_the_file));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            Row(3, "24008386", "MED03", "MED04"));
    }

    [Fact]
    public async Task A_level_code_nobody_has_declared_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_level_code_nobody_has_declared_refuses_the_file));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        var other = Enrolled(db, "24008387");
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            Row(3, "24008387", "MED03", "MDME9"));

        other.OutcomeSource.Should().BeNull();
    }

    /// <summary>
    /// The file says one level, the registration on record says another. A verdict written onto the
    /// wrong registration is not recoverable, so this is a refusal rather than a skip — and it did
    /// not happen once in the 6 810 checkable lines of the real file, so the strictness costs nothing.
    /// </summary>
    [Fact]
    public async Task A_level_disagreeing_with_the_registration_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_level_disagreeing_with_the_registration_refuses_the_file));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        var misfiled = Enrolled(db, "24008387", Med4LevelId);
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            Row(3, "24008387", "MED03", "MED04"));

        misfiled.OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task Retrait_is_not_a_level_anyone_is_reinscribed_into()
    {
        await using var db = TestHarness.NewContext(nameof(Retrait_is_not_a_level_anyone_is_reinscribed_into));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        var withdrawn = Enrolled(db, "24008387", RetraitLevelId);
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            Row(3, "24008387", "MED00", "MED04"));
    }

    [Fact]
    public async Task A_destination_below_the_level_left_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_destination_below_the_level_left_refuses_the_file));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        var goingBackwards = Enrolled(db, "24008387", Med4LevelId);
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            Row(3, "24008387", "MED04", "MED03"));
    }

    [Fact]
    public async Task A_line_with_no_code_refuses_the_file()
    {
        await using var db = TestHarness.NewContext(nameof(A_line_with_no_code_refuses_the_file));
        SeedCatalogue(db);
        var control = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        await RefusesWholeFile(db, control,
            Row(2, "24008386", "MED03", "MED04"),
            new ReinscriptionSheetRow(3, null, "Sans", "Code", "MED03", "MED04"));
    }

    // ---------------------------------------------------------------------------------------------
    // Scope, reporting and access
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Registrations the file does not mention are left alone and counted. That is the opposite of
    /// the déliberation's exceptions canvas: this file is the roll of who <em>is</em> coming back, so
    /// silence means somebody is not, and PGSH cannot tell a graduate from an exclusion.
    /// </summary>
    [Fact]
    public async Task Registrations_the_file_never_mentions_are_untouched_and_counted()
    {
        await using var db = TestHarness.NewContext(nameof(Registrations_the_file_never_mentions_are_untouched_and_counted));
        SeedCatalogue(db);
        Enrolled(db, "24008386");
        var absent = Enrolled(db, "24008387");
        await db.SaveChangesAsync();

        var statusBefore = absent.Status;

        var result = await Apply(db, Row(2, "24008386", "MED03", "MED04"));

        result.IsSuccess.Should().BeTrue();
        result.Value.NotCovered.Should().Be(1);
        absent.Status.Should().Be(statusBefore, "silence in this file is not a verdict");
        absent.OutcomeSource.Should().BeNull();
    }

    /// <summary>The dry run is the plan: same numbers, and nothing written.</summary>
    [Fact]
    public async Task The_preview_reports_what_the_apply_would_do_and_writes_nothing()
    {
        await using var db = TestHarness.NewContext(nameof(The_preview_reports_what_the_apply_would_do_and_writes_nothing));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(
            new PreviewReinscriptionSheetQuery(
                [Row(2, "24008386", "MED03", "MED04")], TestHarness.CurrentYearId, NextYearId),
            default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.WillRegister.Should().Be(1);
        preview.Value.CanApply.Should().BeTrue();
        preview.Value.ByTargetLevel.Should().ContainKey("4ème année");

        source.OutcomeSource.Should().BeNull();
        (await db.Registrations.AnyAsync(r => r.AcademicYearId == NextYearId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_target_year_that_is_not_later_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_target_year_that_is_not_later_is_refused));
        SeedCatalogue(db);
        Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            new ApplyReinscriptionSheetCommand(
                [Row(2, "24008386", "MED03", "MED04")], NextYearId, TestHarness.CurrentYearId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReinscriptionSheet.TargetYearNotLater");
    }

    [Fact]
    public async Task An_empty_file_is_refused_rather_than_reported_as_a_clean_run()
    {
        await using var db = TestHarness.NewContext(nameof(An_empty_file_is_refused_rather_than_reported_as_a_clean_run));
        SeedCatalogue(db);
        await db.SaveChangesAsync();

        var result = await Apply(db);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReinscriptionSheet.Empty");
    }

    [Fact]
    public async Task Only_the_administration_may_apply_a_roll()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_administration_may_apply_a_roll));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386");
        await db.SaveChangesAsync();

        var handler = new ApplyReinscriptionSheetCommandHandler(
            db, Planner(db), new RegistrationCnpnStamper(db, new CnpnAssignment(db)),
            db.StrangerAuthorizer());

        var result = await handler.Handle(
            new ApplyReinscriptionSheetCommand(
                [Row(2, "24008386", "MED03", "MED04")], TestHarness.CurrentYearId, NextYearId),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ReinscriptionSheet.NotAllowed");
        source.OutcomeSource.Should().BeNull();
    }

    /// <summary>
    /// The rollover is the act an effectivity rule authored over the summer bites on: the new
    /// registration is stamped as it is created, not by anybody remembering to run a command.
    /// </summary>
    [Fact]
    public async Task The_registration_it_creates_is_stamped_with_its_governing_text()
    {
        await using var db = TestHarness.NewContext(nameof(The_registration_it_creates_is_stamped_with_its_governing_text));
        SeedCatalogue(db);
        var source = Enrolled(db, "24008386", cnpnVersionId: TestHarness.NewCnpnId);
        source.Student.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        (await Apply(db, Row(2, "24008386", "MED03", "MED04"))).IsSuccess.Should().BeTrue();

        var created = await db.Registrations
            .SingleAsync(r => r.AcademicYearId == NextYearId && r.StudentId == source.StudentId);

        created.CnpnVersionId.Should().Be(TestHarness.NewCnpnId);
        created.CnpnSource.Should().NotBeNull();
    }
}
