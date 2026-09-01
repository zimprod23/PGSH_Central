using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Students.Registrations.Create;
using PGSH.Application.Students.Registrations.CreateMany;
using PGSH.Application.Students.Registrations.FinalYear;
using PGSH.Application.Students.Registrations.Reinscription;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « On ne commence pas la dernière année tant que tout ce qui précède n'est pas validé. »
///
/// <para>The faculty's rule, not an inference: a 7ᵉ année under arrêté 2174.18 and a 6ᵉ under 1650.25
/// cannot be entered while a stage from an earlier year is still unvalidated. The exceptions are real
/// too, which is why the refusal is waivable rather than absolute — and why the waiver is a row with
/// a reason on it rather than a flag.</para>
///
/// <para>The cases that decide the design are the ones where the gate must <b>not</b> fire: an
/// unmarked stage is not a failed one, a stage validated on any registration is done for good, and a
/// student PGSH holds no CNPN for has no final year it can name.</para>
/// </summary>
public class FinalYearGateTests
{
    private const int Year2026 = 31;
    private const int Level4 = 32;   // 4ème année — the last year of the seeded 4-year text
    private const int OldText = TestHarness.OldCnpnId;

    /// <summary>
    /// A four-year text so the 4ᵉ année is the final one, the 3ᵉ année of <c>SeedCatalog</c> below it,
    /// and a year to roll into.
    /// </summary>
    private static ApplicationDbContext Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        db.SeedCatalog();
        db.SeedLevel(Level4, "4ème année", year: 4);
        db.SeedAcademicYear(Year2026, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        // TotalYears = 4, so entering the 4ᵉ année is entering the last year. Through the aggregate,
        // not by assignment: a text's span is only movable by the act that checks it does not fall
        // below a level the text already carries requirements or an effectivity rule for.
        Reshape(db, totalYears: 4);

        db.SaveChanges();
        return db;
    }

    /// <summary>Moves the seeded text's span, the only way a text's span moves.</summary>
    private static void Reshape(ApplicationDbContext db, int totalYears)
    {
        var text = db.CnpnVersions.Local.First(v => v.Id == OldText);

        text.Correct(text.Code, text.Label, totalYears, text.Reference,
                text.AppliesToEntrantsFromAcademicYearId, CnpnSpanFloor.None)
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>A student closed « Admis » in the 3ᵉ année, stamped with the four-year text.</summary>
    private static Registration SeedAdmis(ApplicationDbContext db, string last)
    {
        var registration = db.SeedRegistration("Amine", last);
        registration.Student.AssignCnpnVersion(OldText, isInferred: false);
        registration.StampCnpnVersion(OldText, RegistrationCnpnSource.Backfilled);
        registration.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        return registration;
    }

    /// <summary>
    /// An attempt at the shared stage, driven through the real lifecycle rather than by setting
    /// <c>Result</c> — which is derived, and private for exactly that reason. A mark below 10 lands
    /// <c>NonValidé</c>, at or above it <c>Validé</c>; <paramref name="mark"/> null leaves the
    /// assignment unevaluated, which is the case the gate must ignore.
    /// </summary>
    private static void SeedAttempt(ApplicationDbContext db, Registration registration, decimal? mark)
    {
        var stage = db.Stages.Local.First(s => s.Id == TestHarness.StageId);
        var cohort = db.Cohorts.Local.FirstOrDefault(c => c.StageId == stage.Id)
            ?? db.SeedCohort(stage, groupId: 900, groupLabel: "G900");
        var service = db.Services.Local.FirstOrDefault()
            ?? db.SeedService(1, "Service de Cardiologie");

        if (mark is { } value)
            db.SeedGradedAssignment(registration, cohort, service, value);
        else
            db.SeedAssignment(registration, cohort);
    }

    private static ReinscriptionPlanner Planner(ApplicationDbContext db) =>
        new(db, new OutstandingStageFinder(db));

    private static ApplyReinscriptionCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, Planner(db), new RegistrationCnpnStamper(db, new CnpnAssignment(db)), db.AdminAuthorizer());

    private static ApplyReinscriptionCommand Rollover() =>
        new(TestHarness.CurrentYearId, Year2026, TestHarness.LevelId);

    // =============================================================================================
    // The rule
    // =============================================================================================

    [Fact]
    public async Task An_unvalidated_earlier_stage_blocks_entry_into_the_final_year()
    {
        await using var db = Seed(nameof(An_unvalidated_earlier_stage_blocks_entry_into_the_final_year));
        var admis = SeedAdmis(db, "Bennani");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRegister.Should().Be(0);
        result.Value.FinalYearBlocked.Should().Be(1);
        result.Value.NeedsAttention.Should().Be(1);
        result.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.FinalYearBlocked);

        // ⚠ The assertion that matters: the refusal has to precede the write.
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(0);
    }

    /// <summary>
    /// The control. Same debt, but the year he is entering is not his last — so nothing is blocked,
    /// and a student carries an unvalidated stage forward exactly as he always could.
    /// </summary>
    [Fact]
    public async Task The_same_debt_does_not_block_a_year_that_is_not_the_last()
    {
        await using var db = Seed(nameof(The_same_debt_does_not_block_a_year_that_is_not_the_last));

        // Five-year text: the 4ᵉ année is no longer the final one.
        Reshape(db, totalYears: 5);

        var admis = SeedAdmis(db, "Alaoui");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.Value.WillRegister.Should().Be(1);
        result.Value.FinalYearBlocked.Should().Be(0);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(1);
    }

    /// <summary>
    /// ⚠ <c>NonÉvalué</c> is a stage nobody marked, not a stage he failed. This base holds almost no
    /// marks, so counting it as owed would block the entire faculty on missing data rather than on
    /// anything a student did.
    /// </summary>
    [Fact]
    public async Task An_unmarked_stage_is_not_a_debt()
    {
        await using var db = Seed(nameof(An_unmarked_stage_is_not_a_debt));
        var admis = SeedAdmis(db, "Chraibi");
        SeedAttempt(db, admis, mark: null);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.Value.WillRegister.Should().Be(1);
        result.Value.FinalYearBlocked.Should().Be(0);
    }

    /// <summary>A stage once acquired is never repeated, so one validated attempt clears it for good.</summary>
    [Fact]
    public async Task A_stage_validated_on_any_attempt_is_not_a_debt()
    {
        await using var db = Seed(nameof(A_stage_validated_on_any_attempt_is_not_a_debt));
        var admis = SeedAdmis(db, "Idrissi");
        SeedAttempt(db, admis, mark: 7);
        SeedAttempt(db, admis, mark: 14);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.Value.WillRegister.Should().Be(1);
        result.Value.FinalYearBlocked.Should().Be(0);
    }

    /// <summary>
    /// No text on record means no <c>TotalYears</c>, and a student nobody has stamped must not be
    /// blocked by a number PGSH does not have — the same standing-aside the déliberation applies to
    /// « Diplômé ».
    /// </summary>
    [Fact]
    public async Task A_student_with_no_cnpn_on_record_is_not_blocked()
    {
        await using var db = Seed(nameof(A_student_with_no_cnpn_on_record_is_not_blocked));

        var admis = db.SeedRegistration("Amine", "Fassi");
        admis.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.Value.Rows.Single().Action.Should().Be(ReinscriptionAction.WillRegister);
        result.Value.WillRegister.Should().Be(1);
        result.Value.FinalYearBlocked.Should().Be(0);
    }

    // =============================================================================================
    // The exception
    // =============================================================================================

    [Fact]
    public async Task A_waiver_lets_him_through_and_is_counted_in_the_report()
    {
        await using var db = Seed(nameof(A_waiver_lets_him_through_and_is_counted_in_the_report));
        var admis = SeedAdmis(db, "Jaidi");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var granted = await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "Décision du conseil, PV du 3 juillet"),
            default);

        granted.IsSuccess.Should().BeTrue();

        var result = await ApplyHandler(db).Handle(Rollover(), default);

        result.Value.WillRegister.Should().Be(1);
        result.Value.FinalYearBlocked.Should().Be(0);
        // The override is visible in the same report as the rule it bends.
        result.Value.FinalYearWaived.Should().Be(1);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(1);
    }

    /// <summary>What was owed is captured at the moment of granting, not recomputed on read.</summary>
    [Fact]
    public async Task A_waiver_records_what_it_excused()
    {
        await using var db = Seed(nameof(A_waiver_records_what_it_excused));
        var admis = SeedAdmis(db, "Kettani");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "Dérogation exceptionnelle"), default);

        var waiver = await db.FinalYearEntryWaivers.AsNoTracking().SingleAsync();
        waiver.OutstandingAtGrant.Should().Be(1);
        waiver.OutstandingSummary.Should().Contain("Cardiologie");
        waiver.Reason.Should().Be("Dérogation exceptionnelle");
    }

    /// <summary>
    /// A waiver against no debt would sit in the record asserting an exception that never happened —
    /// and would pre-authorise one the student has not yet incurred.
    /// </summary>
    [Fact]
    public async Task A_waiver_is_refused_when_nothing_is_owed()
    {
        await using var db = Seed(nameof(A_waiver_is_refused_when_nothing_is_owed));
        var admis = SeedAdmis(db, "Lahlou");
        await db.SaveChangesAsync();

        var result = await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "Par précaution"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinalYearWaiver.NotNeeded");
    }

    [Fact]
    public async Task A_second_waiver_for_the_same_year_is_refused()
    {
        await using var db = Seed(nameof(A_second_waiver_for_the_same_year_is_refused));
        var admis = SeedAdmis(db, "Mernissi");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var command = new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "PV du conseil");
        await GrantHandler(db).Handle(command, default);
        var second = await GrantHandler(db).Handle(command, default);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("FinalYearWaiver.AlreadyGranted");
    }

    /// <summary>
    /// Once the registration it permitted exists, the waiver is that year's justification. Revoking it
    /// would leave a student in a final year with an unvalidated stage and nothing saying who allowed
    /// it — the exact state the feature prevents.
    /// </summary>
    [Fact]
    public async Task A_waiver_that_has_been_used_cannot_be_revoked()
    {
        await using var db = Seed(nameof(A_waiver_that_has_been_used_cannot_be_revoked));
        var admis = SeedAdmis(db, "Naciri");
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var granted = await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "PV du conseil"), default);

        var revoke = new RevokeFinalYearWaiverCommandHandler(db, db.AdminAuthorizer());

        // Before the rollover it is still withdrawable.
        var before = await revoke.Handle(new RevokeFinalYearWaiverCommand(granted.Value), default);
        before.IsSuccess.Should().BeTrue();

        // Grant it again, roll over, then try to withdraw it.
        var again = await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(admis.StudentId, Year2026, "PV du conseil"), default);
        await ApplyHandler(db).Handle(Rollover(), default);

        var after = await revoke.Handle(new RevokeFinalYearWaiverCommand(again.Value), default);
        after.IsFailure.Should().BeTrue();
        after.Error.Code.Should().Be("FinalYearWaiver.AlreadyUsed");
    }

    // =============================================================================================
    // The manual path
    // =============================================================================================

    /// <summary>
    /// ⚠ The gate has to bite here too. A rule the bulk rollover enforces and the manual registration
    /// form does not is a rule anybody can step around by using the other button.
    /// </summary>
    [Fact]
    public async Task Creating_the_registration_by_hand_is_refused_the_same_way()
    {
        await using var db = Seed(nameof(Creating_the_registration_by_hand_is_refused_the_same_way));
        var admis = SeedAdmis(db, "Ouazzani");
        admis.Student.AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram.Medecine;
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var handler = new CreateRegistrationCommandHandler(
            db,
            new RegistrationCnpnStamper(db, new CnpnAssignment(db)),
            new FinalYearGuard(db, new OutstandingStageFinder(db)));

        var result = await handler.Handle(
            new CreateRegistrationCommand(admis.StudentId, Year2026, Level4, RegistrationStatus.Active),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.FinalYearBlocked");
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(0);
    }

    // =============================================================================================
    // The bulk path — the same rule, asked once for the batch
    // =============================================================================================

    /// <summary>
    /// ⚠ Batching the question must not batch the answer. Three students, three different verdicts:
    /// the gate is asked in one pass and still decides per student, because whether this is somebody's
    /// last year is a fact about his own text.
    /// </summary>
    [Fact]
    public async Task The_bulk_path_refuses_only_the_students_the_gate_names()
    {
        await using var db = Seed(nameof(The_bulk_path_refuses_only_the_students_the_gate_names));

        var blocked = SeedAdmis(db, "Ouazzani");
        SeedAttempt(db, blocked, mark: 7);

        var waived = SeedAdmis(db, "Sqalli");
        SeedAttempt(db, waived, mark: 7);

        // The control: same promotion, same batch, nothing owed. A refusal test with no control
        // passes just as well when the whole call fails.
        var clear = SeedAdmis(db, "Tazi");
        SeedAttempt(db, clear, mark: 14);
        await db.SaveChangesAsync();

        await GrantHandler(db).Handle(
            new GrantFinalYearWaiverCommand(waived.StudentId, Year2026, "PV du conseil"), default);

        var result = await BulkHandler(db).Handle(
            new CreateManyRegistrationsCommand(
                [blocked.StudentId, waived.StudentId, clear.StudentId], Year2026, Level4),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SuccessCount.Should().Be(2);

        var refused = result.Value.Items.Single(i => !i.IsSuccess);
        refused.Identifier.Should().Be(blocked.StudentId);
        refused.Error!.Code.Should().Be("Registrations.FinalYearBlocked");

        // ⚠ The refusal has to precede the write, and the two others still have to land.
        var written = await db.Registrations
            .Where(r => r.AcademicYearId == Year2026)
            .Select(r => r.StudentId)
            .ToListAsync();

        written.Should().BeEquivalentTo(new[] { waived.StudentId, clear.StudentId });
    }

    /// <summary>
    /// The batched lookup returns a <c>Dictionary&lt;Guid, int&gt;</c> of final years, whose default is
    /// <b>0</b> — read as "his text runs 0 years" it makes every year somebody's last, and blocks
    /// hardest the one student the gate must stand aside for.
    /// </summary>
    [Fact]
    public async Task The_bulk_path_stands_aside_for_a_student_with_no_cnpn_on_record()
    {
        await using var db = Seed(nameof(The_bulk_path_stands_aside_for_a_student_with_no_cnpn_on_record));

        var unstamped = db.SeedRegistration("Amine", "Fassi");
        SeedAttempt(db, unstamped, mark: 7);
        await db.SaveChangesAsync();

        var result = await BulkHandler(db).Handle(
            new CreateManyRegistrationsCommand([unstamped.StudentId], Year2026, Level4), default);

        result.Value.SuccessCount.Should().Be(1);
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(1);
    }

    /// <summary>
    /// How long a cursus runs is read from the text on his most recent registration first, and only
    /// then from the stamp he happens to carry now — the order every reader has used since the text
    /// became a property of the registration. Here the two disagree and only the registration's answer
    /// makes the 4ᵉ année his last.
    /// </summary>
    [Fact]
    public async Task The_bulk_path_reads_the_text_that_governed_the_year_he_is_leaving()
    {
        await using var db = Seed(nameof(The_bulk_path_reads_the_text_that_governed_the_year_he_is_leaving));

        var admis = db.SeedRegistration("Amine", "Zniber");
        admis.Student.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        admis.StampCnpnVersion(OldText, RegistrationCnpnSource.Backfilled);
        admis.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        SeedAttempt(db, admis, mark: 7);
        await db.SaveChangesAsync();

        var result = await BulkHandler(db).Handle(
            new CreateManyRegistrationsCommand([admis.StudentId], Year2026, Level4), default);

        result.Value.SuccessCount.Should().Be(0);
        result.Value.Items.Single().Error!.Code.Should().Be("Registrations.FinalYearBlocked");
        (await db.Registrations.CountAsync(r => r.AcademicYearId == Year2026)).Should().Be(0);
    }

    private static CreateManyRegistrationsCommandHandler BulkHandler(ApplicationDbContext db) =>
        new(db,
            new RegistrationCnpnStamper(db, new CnpnAssignment(db)),
            new FinalYearGuard(db, new OutstandingStageFinder(db)));

    private static GrantFinalYearWaiverCommandHandler GrantHandler(ApplicationDbContext db) =>
        new(db,
            new OutstandingStageFinder(db),
            TestHarness.UserContext(Guid.NewGuid(), Roles.Scolarite),
            db.AdminAuthorizer());
}
