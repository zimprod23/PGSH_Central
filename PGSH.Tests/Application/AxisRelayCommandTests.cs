using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// L'acte de rattrapage, bout en bout : lire l'axe, dériver la longueur d'une colonne, reposer les
/// colonnes, et faire suivre les rotations publiées.
///
/// <para>L'arithmétique est éprouvée à part et exhaustivement (<c>AxisRelayPlannerTests</c>, pur).
/// Ce qui se vérifie <b>ici</b> est ce qu'un test pur ne peut pas voir : que les écritures passent par
/// les agrégats, que les deux comptes confirmés mordent, qu'un refus n'écrit rien, et qu'une rotation
/// que l'acte ne peut pas rattraper est comptée plutôt que de faire échouer tout le monde.</para>
/// </summary>
public class AxisRelayCommandTests
{
    private const int ServiceId = 40;
    private const int SlotP1 = 7101, SlotP2 = 7102, SlotP3 = 7103;
    private const int CellP1 = 7201, CellP2 = 7202, CellP3 = 7203;

    /// <summary>Trois colonnes de 10 jours ouvrables, bout à bout depuis le lundi 5 janvier 2026.</summary>
    private static readonly DateOnly P1Start = new(2026, 1, 5),  P1End = new(2026, 1, 16);
    private static readonly DateOnly P2Start = new(2026, 1, 19), P2End = new(2026, 1, 30);
    private static readonly DateOnly P3Start = new(2026, 2, 2),  P3End = new(2026, 2, 13);

    /// <summary>Une semaine d'examens au milieu de P1 : cinq jours ouvrables pris.</summary>
    private static readonly DateOnly WindowFrom = new(2026, 1, 12), WindowTo = new(2026, 1, 16);

    /// <summary>Le jour où l'acte est joué : P1 est en cours, P2 et P3 sont à venir.</summary>
    private static readonly DateOnly Today = new(2026, 1, 8);

    private sealed record Fixture(
        ApplicationDbContext Db, RecordingAuditTrail Audit, int YearId, int LevelId);

    /// <summary>
    /// Une promotion à trois colonnes, une cohorte, un étudiant, une période par colonne — et la
    /// fenêtre d'examens déclarée.
    /// </summary>
    private static async Task<Fixture> SeedAsync(string name, bool declareWindow = true)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();
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

        // ⚠ Démarrées toutes les trois : c'est ce que fait Start(), un whole-student start. P2 et P3
        // portent donc le drapeau alors que leurs fenêtres sont à venir — le cas mesuré sur 305
        // périodes de la 4ᵉ MED, et celui que MovableOn existe pour trancher.
        var one = db.SeedPeriod(assignment, service, P1Start, P1End, started: true);
        var two = db.SeedPeriod(assignment, service, P2Start, P2End, started: true);
        var three = db.SeedPeriod(assignment, service, P3Start, P3End, started: true);

        db.SeedCoverage(one, c1);
        db.SeedCoverage(two, c2);
        db.SeedCoverage(three, c3);

        int yearId = cohort.AcademicGroup.AcademicYearId;
        int levelId = stage.LevelId;

        if (declareWindow)
            db.PromotionPauses.Add(new PromotionPause
            {
                AcademicYearId = yearId, LevelId = levelId,
                StartDate = WindowFrom, EndDate = WindowTo,
                Kind = PauseKind.Exam, Reason = "Examens du 1er semestre",
            });

        await db.SaveChangesAsync();
        return new Fixture(db, new RecordingAuditTrail(), yearId, levelId);
    }

    private static AxisRelayReader Reader(ApplicationDbContext db) =>
        new(db, new WorkingDayProvider(db));

    private static ApplyAxisRelayCommandHandler Handler(Fixture f) =>
        new(f.Db, new AcademicYearResolver(f.Db), Reader(f.Db), f.Audit, TestHarness.ClockOn(Today));

    private static PreviewAxisRelayQueryHandler PreviewHandler(Fixture f) =>
        new(new AcademicYearResolver(f.Db), Reader(f.Db), TestHarness.ClockOn(Today));

    private static ApplyAxisRelayCommand Apply(Fixture f, int slots, int periods) =>
        new(f.LevelId, slots, periods, f.YearId);

    // ─── L'aperçu ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_preview_derives_the_column_length_and_writes_nothing()
    {
        var f = await SeedAsync(nameof(The_preview_derives_the_column_length_and_writes_nothing));

        var result = await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        var preview = result.Value;

        preview.ColumnLength.Should().Be(10, "P2 and P3 still hold their full ten");
        preview.ColumnsAgreeingOnLength.Should().Be(2);
        preview.FromPeriodNumber.Should().Be(1, "the window falls inside P1");
        preview.WorkingDaysChanged.Should().Be(5);

        var slot = await f.Db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP1);
        slot.EndDate.Should().Be(P1End, "a preview writes nothing");
    }

    /// <summary>
    /// ⚠ P1 est en cours : elle s'allonge. P2 et P3 sont à venir bien que <c>IsStarted</c> — elles se
    /// déplacent. C'est toute la distinction `Extendable` / `MovableOn` vue depuis l'acte.
    /// </summary>
    [Fact]
    public async Task The_running_column_is_extended_and_the_future_ones_are_moved()
    {
        var f = await SeedAsync(nameof(The_running_column_is_extended_and_the_future_ones_are_moved));

        var result = await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default);

        result.Value.PeriodsToExtend.Should().Be(1, "P1 has begun — its start is a fact");
        result.Value.PeriodsToMove.Should().Be(2, "P2 and P3 have not, whatever the flag says");
        result.Value.PeriodsBlocked.Should().Be(0);
    }

    // ─── L'application ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Applying_relays_the_columns_and_the_periods_follow()
    {
        var f = await SeedAsync(nameof(Applying_relays_the_columns_and_the_periods_follow));

        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        var result = await Handler(f).Handle(
            Apply(f, preview.SlotsAffected, preview.PeriodsAffected), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        result.Value.PeriodsExtended.Should().Be(1);
        result.Value.PeriodsMoved.Should().Be(2);
        result.Value.WorkingDaysChanged.Should().Be(5);

        var slots = await f.Db.StageSlots.AsNoTracking().OrderBy(s => s.PeriodNumber).ToListAsync();
        slots[0].StartDate.Should().Be(P1Start, "P1 is underway — its start must not move");
        slots[0].EndDate.Should().BeAfter(P1End);
        slots[1].StartDate.Should().BeAfter(P2Start, "the cascade pushed it");

        // ⚠ La grille et les dossiers doivent dire la même chose — c'est l'état partiel que la
        // transaction existe pour empêcher.
        var periods = await f.Db.ServicePeriods.AsNoTracking().OrderBy(p => p.StartDate).ToListAsync();
        periods[0].StartDate.Should().Be(slots[0].StartDate);
        periods[0].EndDate.Should().Be(slots[0].EndDate);
        periods[1].StartDate.Should().Be(slots[1].StartDate);
    }

    /// <summary>
    /// ⚠ Le passage par les agrégats se voit à l'événement : écrire les dates à plat sur le contexte
    /// marcherait et n'en lèverait aucun.
    /// </summary>
    [Fact]
    public async Task Every_window_that_really_moves_raises_its_event()
    {
        var f = await SeedAsync(nameof(Every_window_that_really_moves_raises_its_event));
        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        await Handler(f).Handle(Apply(f, preview.SlotsAffected, preview.PeriodsAffected), default);

        var assignment = await f.Db.InternshipAssignments.SingleAsync();
        assignment.DomainEvents
            .OfType<ServicePeriodRescheduledDomainEvent>()
            .Should().HaveCount(3, "one per rotation whose window actually changed");
    }

    /// <summary>⚠ Le registre doit porter <em>combien</em>, pas seulement « l'axe a été reposé ».</summary>
    [Fact]
    public async Task The_register_carries_what_the_act_actually_did()
    {
        var f = await SeedAsync(nameof(The_register_carries_what_the_act_actually_did));
        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        await Handler(f).Handle(Apply(f, preview.SlotsAffected, preview.PeriodsAffected), default);

        f.Audit.Fields["periodsExtended"].Should().Be(1);
        f.Audit.Fields["periodsMoved"].Should().Be(2);
        f.Audit.Fields["workingDaysChanged"].Should().Be(5);
        f.Audit.Fields["columnLength"].Should().Be(10);
    }

    // ─── Les refus ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>Deux nombres, confirmés séparément.</b> Ils bougent pour des raisons différentes, et un
    /// seul en laisserait passer la moitié.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_confirmed_count_that_no_longer_matches_refuses(bool onPeriods)
    {
        var f = await SeedAsync($"relay-mismatch-{onPeriods}");
        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        var command = onPeriods
            ? Apply(f, preview.SlotsAffected, preview.PeriodsAffected + 7)
            : Apply(f, preview.SlotsAffected + 7, preview.PeriodsAffected);

        var result = await Handler(f).Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.RelayCountMismatch");

        // ⚠ L'assertion qui compte : un refus n'écrit rien. Un garde placé après l'écriture rendrait
        // le même Result et passerait le test précédent.
        var slot = await f.Db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP1);
        slot.EndDate.Should().Be(P1End);
        var period = await f.Db.ServicePeriods.AsNoTracking()
            .OrderBy(p => p.StartDate).FirstAsync();
        period.EndDate.Should().Be(P1End);
    }

    /// <summary>
    /// ⚠ « Rien à rattraper » est un refus, pas un succès à zéro : le premier veut dire que la
    /// promotion va bien, et les deux se liraient pareil dans un résultat.
    /// </summary>
    [Fact]
    public async Task An_axis_no_window_touches_is_refused_rather_than_relaid_to_no_effect()
    {
        var f = await SeedAsync(
            nameof(An_axis_no_window_touches_is_refused_rather_than_relaid_to_no_effect),
            declareWindow: false);

        var result = await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.NothingToRecover");
    }

    /// <summary>
    /// ⚠ Une rotation notée ne se rattrape pas — et n'empêche pas l'acte. Elle est comptée, sinon ce
    /// qui a été rattrapé et ce qui ne l'a pas été se liraient pareil.
    /// </summary>
    [Fact]
    public async Task A_marked_rotation_is_counted_as_blocked_and_the_rest_still_moves()
    {
        var f = await SeedAsync(nameof(A_marked_rotation_is_counted_as_blocked_and_the_rest_still_moves));

        var last = await f.Db.ServicePeriods.OrderBy(p => p.StartDate).LastAsync();
        last.IsComplete = true;
        last.Evaluation = new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 15m };
        await f.Db.SaveChangesAsync();

        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        preview.PeriodsBlocked.Should().Be(1);
        preview.PeriodsAffected.Should().Be(2, "the other two are still worth pushing");

        var result = await Handler(f).Handle(
            Apply(f, preview.SlotsAffected, preview.PeriodsAffected), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        result.Value.PeriodsBlocked.Should().Be(1);

        var marked = await f.Db.ServicePeriods.AsNoTracking().SingleAsync(p => p.Id == last.Id);
        marked.EndDate.Should().Be(P3End, "a marked rotation keeps the dates it was marked on");
    }

    /// <summary>
    /// ⚠ Une colonne déplacée à la main ancre : le recalcul la laisse et compte ce qu'il a épargné.
    /// </summary>
    [Fact]
    public async Task A_hand_moved_column_is_left_where_the_human_put_it()
    {
        var f = await SeedAsync(nameof(A_hand_moved_column_is_left_where_the_human_put_it));

        var third = await f.Db.StageSlots.SingleAsync(s => s.Id == SlotP3);
        third.MoveTo(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 13));
        await f.Db.SaveChangesAsync();

        var preview = (await PreviewHandler(f).Handle(
            new PreviewAxisRelayQuery(f.LevelId, f.YearId), default)).Value;

        preview.ColumnsAnchored.Should().Be(1);

        await Handler(f).Handle(Apply(f, preview.SlotsAffected, preview.PeriodsAffected), default);

        var kept = await f.Db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP3);
        kept.StartDate.Should().Be(new DateOnly(2026, 3, 2));
        kept.IsMovedByHand.Should().BeTrue();
    }
}
