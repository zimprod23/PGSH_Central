using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Common.Utils;
using PGSH.Infrastructure.Database;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Phase 17.1 — « on est en P3, peut-on changer P7 ? » — and specifically the half that makes it hard:
/// a <see cref="StageRotationMode.SingleService"/> run, where one <c>ServicePeriod</c> spans several
/// columns.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This is where « offset by the delta » and « re-derive from the cells » stop agreeing</b>,
/// and the difference is not cosmetic. A run of three columns is one stay of one length; moving its
/// middle column changes nothing about when the student arrives or leaves, so an offset would write a
/// stay that is a week longer than the one that happens. Re-deriving min/max over the covered cells —
/// the same rule <c>CohortStayFolder</c> applies when publishing — is the only version that makes a
/// move and a fresh publication produce the same dates.</para>
///
/// <para>The 5ᵉ and 6ᵉ années are <c>SingleService</c> in 51 923 of 51 924 imported placements, so this
/// is not the exotic mode — it is most of the faculty.</para>
/// </remarks>
public class PublishedColumnMoveTests
{
    private const int SlotP1 = 6101, SlotP2 = 6102, SlotP3 = 6103;
    private const int CellP1 = 6201, CellP2 = 6202, CellP3 = 6203;
    private const int ServiceId = 30;

    private static readonly DateOnly P1Start = new(2026, 3, 2),  P1End = new(2026, 3, 13);
    private static readonly DateOnly P2Start = new(2026, 3, 16), P2End = new(2026, 3, 27);
    private static readonly DateOnly P3Start = new(2026, 3, 30), P3End = new(2026, 4, 10);

    /// <summary>
    /// Un jour <b>à l'intérieur</b> du séjour. ⚠ Depuis que la mobilité est datée
    /// (<c>ServicePeriodLifecycle.MovableOn</c>), « cette rotation a commencé » ne se pose plus par
    /// <c>IsStarted</c> — <c>Start()</c> est un whole-student start et pose le drapeau sur des séjours
    /// encore à des mois. Ce qui la rend immobile est que sa fenêtre a commencé.
    /// </summary>
    private static readonly DateOnly Underway = new(2026, 3, 10);

    /// <summary>
    /// One cohorte standing in one service for three consecutive columns: a single
    /// <c>ServicePeriod</c> spanning P1→P3, with one coverage row per column.
    /// </summary>
    private static async Task<ApplicationDbContextHolder> SeedRunAsync(string name)
    {
        var db = TestHarness.NewContext(name);

        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;

        var service = db.SeedService(ServiceId, "Cardiologie A");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        var p1 = db.SeedSlot(stage, SlotP1, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, SlotP2, 2, P2Start, P2End);
        var p3 = db.SeedSlot(stage, SlotP3, 3, P3Start, P3End);

        var c1 = db.SeedSlotAssignment(CellP1, cohort, p1, service);
        var c2 = db.SeedSlotAssignment(CellP2, cohort, p2, service);
        var c3 = db.SeedSlotAssignment(CellP3, cohort, p3, service);

        var registration = db.SeedRegistration("Amina", "Benali");
        var assignment = db.SeedAssignment(registration, cohort);

        // One période for the whole run — that is what SingleService means.
        var period = db.SeedPeriod(assignment, service, P1Start, P3End, started: false);
        db.SeedCoverage(period, c1);
        db.SeedCoverage(period, c2, leadCell: false);
        db.SeedCoverage(period, c3, leadCell: false);

        await db.SaveChangesAsync();
        return new ApplicationDbContextHolder(db);
    }

    /// <summary>
    /// ⚠ <b>The case an offset gets wrong.</b> P2 is the middle of the run: moving it leaves the stay
    /// beginning at P1 and ending at P3, exactly as before. The période is <i>covered</i> by the move
    /// and its window does <b>not</b> change — which is why the act reports two numbers.
    /// </summary>
    [Fact]
    public async Task Moving_the_middle_column_of_a_run_does_not_change_the_stay()
    {
        await using var holder = await SeedRunAsync(nameof(Moving_the_middle_column_of_a_run_does_not_change_the_stay));
        var db = holder.Db;

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP2, TestHarness.StageId, null,
                P2Start.AddDays(1), P2End.AddDays(1), ConfirmedPeriodCount: 1),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsCovered.Should().Be(1, "the run's période is touched by the move");
        result.Value.PeriodsShifted.Should().Be(0,
            "a stay begins at its first column and ends at its last — moving the middle moves neither");

        var period = await db.ServicePeriods.AsNoTracking().SingleAsync();
        period.StartDate.Should().Be(P1Start);
        period.EndDate.Should().Be(P3End);
    }

    /// <summary>And the edge case: moving the last column really does extend the stay.</summary>
    /// <summary>
    /// ⚠ <b>Le marqueur doit arriver par le chemin réel, pas seulement depuis l'agrégat.</b> Un test
    /// de domaine prouve que <c>MoveTo</c> marque ; il ne prouve pas que le handler passe par
    /// <c>MoveTo</c>. Avant cette garde le handler écrivait les deux dates à la main, donc un
    /// recalcul d'axe aurait réécrit par-dessus la correction d'un humain sans rien signaler.
    /// </summary>
    [Fact]
    public async Task Moving_a_column_through_the_command_marks_it_moved_by_hand()
    {
        await using var holder = await SeedRunAsync(
            nameof(Moving_a_column_through_the_command_marks_it_moved_by_hand));
        var db = holder.Db;

        (await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP3))
            .IsMovedByHand.Should().BeFalse("the control: the axis laid it");

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7), ConfirmedPeriodCount: 1),
            default);

        result.IsSuccess.Should().BeTrue();

        var moved = await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP3);
        moved.IsMovedByHand.Should().BeTrue();
        moved.StartDate.Should().Be(P3Start.AddDays(7));

        (await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP1))
            .IsMovedByHand.Should().BeFalse("only the column that was moved is marked");
    }

    [Fact]
    public async Task Moving_the_last_column_of_a_run_moves_the_end_of_the_stay()
    {
        await using var holder = await SeedRunAsync(nameof(Moving_the_last_column_of_a_run_moves_the_end_of_the_stay));
        var db = holder.Db;

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7), ConfirmedPeriodCount: 1),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsShifted.Should().Be(1);

        var period = await db.ServicePeriods.AsNoTracking().SingleAsync();
        period.StartDate.Should().Be(P1Start, "the run still begins where it began");
        period.EndDate.Should().Be(P3End.AddDays(7));
    }

    /// <summary>
    /// ⚠ A column dragged past its neighbour would leave a stay whose columns no longer follow one
    /// another. The span would still compute, and it would describe a continuous presence in one
    /// service that never happened — so it is refused rather than written.
    /// </summary>
    [Fact]
    public async Task A_column_dragged_past_its_neighbour_is_refused()
    {
        await using var holder = await SeedRunAsync(nameof(A_column_dragged_past_its_neighbour_is_refused));
        var db = holder.Db;

        // P2 pushed beyond P3's start: P2 and P3 would now overlap inside one stay.
        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP2, TestHarness.StageId, null,
                P3Start.AddDays(1), P3End.AddDays(1), ConfirmedPeriodCount: 1),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotMoveBreaksRun");

        var slot = await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP2);
        slot.StartDate.Should().Be(P2Start, "a refused move writes nothing");
    }

    /// <summary>
    /// ⚠ <b>Attendance is the silent half.</b> A mark announces itself on every screen; a day of
    /// presence is invisible until somebody needs it. Sliding a window out from under recorded days
    /// makes the register lie, so the refusal counts them and says how many.
    /// </summary>
    [Fact]
    public async Task A_column_whose_periods_carry_attendance_cannot_be_moved()
    {
        await using var holder = await SeedRunAsync(nameof(A_column_whose_periods_carry_attendance_cannot_be_moved));
        var db = holder.Db;

        var period = await db.ServicePeriods.SingleAsync();
        // ⚠ No Id: it is a child of an already-tracked parent, so pre-setting a store-generated key
        // makes EF classify it Modified and UPDATE zero rows (see InternshipAssignment.Delocalize).
        period.Attendance.Add(new AttendanceRecord
        {
            ServicePeriodId = period.Id,
            Date = P2Start,
            Status = AttendanceStatus.Present,
        });
        await db.SaveChangesAsync();

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7), ConfirmedPeriodCount: 1),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotPeriodsAlreadyUnderway");
        result.Error.Description.Should().Contain("1 journée(s) de présence",
            "the operator has to be told what stands in the way, not merely that something does");
    }

    /// <summary>
    /// ⚠ <b>Two codes, because they are two situations.</b> A client that never opened the preview —
    /// the grid's old button, which does not know this parameter yet — must not be told that
    /// « something changed since the aperçu » it never saw.
    /// </summary>
    [Fact]
    public async Task Confirming_the_wrong_number_is_a_different_refusal_from_confirming_nothing()
    {
        await using var holder = await SeedRunAsync(
            nameof(Confirming_the_wrong_number_is_a_different_refusal_from_confirming_nothing));
        var db = holder.Db;

        var wrong = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7), ConfirmedPeriodCount: 99),
            default);

        wrong.IsFailure.Should().BeTrue();
        wrong.Error.Code.Should().Be("Schedule.SlotMoveCountMismatch");

        var none = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7)),
            default);

        none.IsFailure.Should().BeTrue();
        none.Error.Code.Should().Be("Schedule.SlotMoveNotConfirmed");
    }

    /// <summary>
    /// ⚠ The preview and the act run the <b>same</b> planner. If they did not, the number the operator
    /// confirms would come from a different arithmetic than the one the guard compares it against —
    /// and the guard would be comparing two unrelated things.
    /// </summary>
    [Fact]
    public async Task The_preview_reports_what_the_act_would_do()
    {
        await using var holder = await SeedRunAsync(nameof(The_preview_reports_what_the_act_would_do));
        var db = holder.Db;

        var preview = await new GetStageSlotMovePreviewQueryHandler(
                db, new PublishedPeriodShifter(db), TestHarness.ClockOn(TestHarness.BeforeAnyWindow))
            .Handle(new GetStageSlotMovePreviewQuery(SlotP3, P3Start.AddDays(7), P3End.AddDays(7)), default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.PeriodsCovered.Should().Be(1);
        preview.Value.PeriodsWhoseWindowMoves.Should().Be(1);
        preview.Value.RefusalMessage.Should().BeNull();

        var act = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7),
                ConfirmedPeriodCount: preview.Value.PeriodsCovered),
            default);

        act.IsSuccess.Should().BeTrue();
        act.Value.PeriodsCovered.Should().Be(preview.Value.PeriodsCovered);
        act.Value.PeriodsShifted.Should().Be(preview.Value.PeriodsWhoseWindowMoves);
    }

    /// <summary>
    /// ⚠ A refusal is an <i>answer</i> from the preview, not an error: the screen has to be able to say
    /// « cette colonne ne peut pas bouger » before the operator fills in a form.
    /// </summary>
    [Fact]
    public async Task The_preview_explains_a_refusal_instead_of_failing()
    {
        await using var holder = await SeedRunAsync(nameof(The_preview_explains_a_refusal_instead_of_failing));
        var db = holder.Db;

        var started = await db.ServicePeriods.SingleAsync();
        started.IsStarted = true;
        await db.SaveChangesAsync();

        var preview = await new GetStageSlotMovePreviewQueryHandler(
                db, new PublishedPeriodShifter(db), TestHarness.ClockOn(Underway))
            .Handle(new GetStageSlotMovePreviewQuery(SlotP3, P3Start.AddDays(7), P3End.AddDays(7)), default);

        preview.IsSuccess.Should().BeTrue("a read that cannot be acted on is still a read");
        preview.Value.RefusalMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// ⚠ <b>Déplacer une rotation publiée est un fait du dossier de l'étudiant, donc un événement.</b>
    /// L'acte réécrivait la fenêtre de milliers de rotations d'un coup et n'en levait aucun, là où bien
    /// plus petit que lui en lève un — et les dates d'avant n'existent nulle part ailleurs une fois la
    /// ligne écrite.
    /// </summary>
    [Fact]
    public async Task Moving_a_published_column_raises_an_event_carrying_both_windows()
    {
        await using var holder = await SeedRunAsync(nameof(Moving_a_published_column_raises_an_event_carrying_both_windows));
        var db = holder.Db;

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP3, TestHarness.StageId, null,
                P3Start.AddDays(7), P3End.AddDays(7), ConfirmedPeriodCount: 1),
            default);

        result.IsSuccess.Should().BeTrue();

        var assignment = await db.InternshipAssignments.SingleAsync();
        var moved = assignment.DomainEvents
            .OfType<ServicePeriodRescheduledDomainEvent>()
            .Should().ContainSingle().Subject;

        moved.FromEndDate.Should().Be(P3End, "sans l'avant, personne ne peut dire de combien cela a bougé");
        moved.ToEndDate.Should().Be(P3End.AddDays(7));
        moved.FromStartDate.Should().Be(P1Start, "le séjour commence toujours à sa première colonne");
        moved.RegistrationId.Should().Be(assignment.RegistrationId);
    }

    /// <summary>
    /// ⚠ <b>Le témoin, et il porte la même distinction que <c>PeriodsShifted</c>.</b> Déplacer la
    /// colonne du *milieu* d'un séjour ne bouge pas le séjour : lever un événement par période
    /// *couverte* ferait lire « des milliers de rotations déplacées » là où aucune ne l'est.
    /// </summary>
    [Fact]
    public async Task Moving_a_column_that_moves_no_stay_raises_nothing()
    {
        await using var holder = await SeedRunAsync(nameof(Moving_a_column_that_moves_no_stay_raises_nothing));
        var db = holder.Db;

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP2, TestHarness.StageId, null,
                P2Start.AddDays(1), P2End.AddDays(1), ConfirmedPeriodCount: 1),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsShifted.Should().Be(0);

        var assignment = await db.InternshipAssignments.SingleAsync();
        assignment.DomainEvents.OfType<ServicePeriodRescheduledDomainEvent>()
            .Should().BeEmpty("rien n'a bougé, donc il ne s'est rien passé à raconter");
    }

    /// <summary>
    /// ⚠ <b>L'agrégat refuse tout seul, sans le planificateur.</b> <c>PlanAsync</c> écarte déjà ces
    /// périodes, donc ce refus ne devrait jamais s'afficher — mais un agrégat qui fait confiance à son
    /// appelant n'a pas d'invariant, il a une convention, et la convention se perd au premier appelant
    /// suivant. Appelé directement, comme le ferait un acte futur.
    /// </summary>
    [Fact]
    public async Task The_aggregate_refuses_to_move_a_rotation_that_has_begun()
    {
        await using var holder = await SeedRunAsync(nameof(The_aggregate_refuses_to_move_a_rotation_that_has_begun));
        var db = holder.Db;

        var assignment = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync();

        var period = assignment.ServicePeriods.Single();
        period.IsStarted = true;

        var moved = assignment.Reschedule(period.Id, P1Start.AddDays(7), P3End.AddDays(7), Underway);

        moved.IsFailure.Should().BeTrue();
        moved.Error.Code.Should().Be("Schedule.PeriodCannotBeRescheduled");
        period.StartDate.Should().Be(P1Start, "un refus n'écrit rien");
    }

    /// <summary>
    /// ⚠ Une fenêtre négative est <b>silencieuse</b> : chaque calcul de durée en aval la lit comme un
    /// nombre de jours négatif et rend des totaux que rien n'annonce comme absurdes.
    /// </summary>
    [Fact]
    public async Task The_aggregate_refuses_a_window_that_ends_before_it_starts()
    {
        await using var holder = await SeedRunAsync(nameof(The_aggregate_refuses_a_window_that_ends_before_it_starts));
        var db = holder.Db;

        var assignment = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync();

        var period = assignment.ServicePeriods.Single();

        var moved = assignment.Reschedule(period.Id, P3End, P1Start, TestHarness.BeforeAnyWindow);

        moved.IsFailure.Should().BeTrue();
        moved.Error.Code.Should().Be("Schedule.PeriodWindowReversed");
    }

    /// <summary>Disposes the context a fixture opened, without every test repeating the ceremony.</summary>
    private sealed class ApplicationDbContextHolder(ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
