using FluentAssertions;
using PGSH.Application.Calendar.Pauses;
using PGSH.Application.Stages.InternshipAssignments;
using PGSH.Application.Stages.InternshipAssignments.GetById;
using PGSH.Application.Stages.InternshipAssignments.GetMany;
using PGSH.Domain.Calendar;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « En examens » plutôt que « En cours », dérivé du calendrier de la promotion.
/// </summary>
/// <remarks>
/// <para><b>Le manque que cela comble, mesuré le 18/09/2026 sur la base vivante :</b> une fenêtre
/// d'examens était déclarée sur la 4ᵉ MED et <b>472 rotations</b> se lisaient « En cours » ce
/// matin-là — c'est-à-dire « cet étudiant est dans son service » — alors que la faculté avait écrit
/// le contraire. Rien nulle part ne le contredisait, parce qu'une fenêtre déclarée n'écrit rien.</para>
///
/// <para>⚠ <b>La propriété qui compte est que rien n'est stocké.</b> C'est ce qui sépare ceci de la
/// pause par étape retirée la veille : là-bas un drapeau était posé sur chaque période, qu'il fallait
/// ensuite enlever, qui s'accumulait au rejeu et qui survivait à la révocation de la fenêtre. Ici,
/// révoquer éteint l'état pour toute la promotion à la lecture suivante, sans un seul écrit — et le
/// dernier cas de ce fichier est ce qui le prouve.</para>
/// </remarks>
public class PromotionSuspensionDisplayTests
{
    private const int OtherLevelId = 7;
    private const int ServiceId = 44;

    private static readonly DateOnly ExamStart = new(2026, 3, 9);
    private static readonly DateOnly ExamEnd = new(2026, 3, 20);
    private static readonly DateOnly DuringExams = new(2026, 3, 16);
    private static readonly DateOnly AfterExams = new(2026, 3, 23);

    private static GetInternshipAssignmentsQueryHandler Handler(ApplicationDbContext db, DateOnly on) =>
        new(db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(on));

    private static void Declare(ApplicationDbContext db, int levelId = TestHarness.LevelId) =>
        db.PromotionPauses.Add(new PromotionPause
        {
            AcademicYearId = TestHarness.CurrentYearId,
            LevelId = levelId,
            StartDate = ExamStart,
            EndDate = ExamEnd,
            Kind = PauseKind.Exam,
            Reason = "Examens du 1er semestre",
            IsConfirmed = true,
            RecordedOn = new DateTime(2025, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        });

    /// <summary>
    /// Une rotation ouverte qui couvre la fenêtre — la forme exacte des 472.
    /// </summary>
    private static ApplicationDbContext Seed(string name, bool started = true)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();
        db.SeedLevel(OtherLevelId, "4ème année", year: 4);

        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        db.SeedPeriod(assignment, service,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 3), started: started);

        if (started)
            assignment.Start();

        return db;
    }

    private static async Task<InternshipAssignmentSummaryResponse> RowAsync(
        ApplicationDbContext db, DateOnly on)
    {
        var page = await Handler(db, on).Handle(new GetInternshipAssignmentsQuery(null, null, null, null), default);
        return page.Value.Items.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task A_rotation_inside_a_declared_window_carries_the_motif()
    {
        await using var db = Seed(nameof(A_rotation_inside_a_declared_window_carries_the_motif));
        Declare(db);
        await db.SaveChangesAsync();

        var row = await RowAsync(db, DuringExams);

        row.Status.Should().Be(InternshipStatus.Ongoing, "le cycle de vie ne change pas");
        row.SuspendedBy.Should().NotBeNull();
        row.SuspendedBy!.Reason.Should().Be("Examens du 1er semestre");
        row.SuspendedBy.EndDate.Should().Be(ExamEnd, "un état sans terme se lit comme un blocage");
        row.SuspendedBy.Kind.Should().Be(PauseKind.Exam);
    }

    /// <summary>
    /// ⚠ Le contrôle qui donne un sens au cas précédent : la même rotation, la même fenêtre, trois
    /// jours plus tard. L'étudiant redevient « En cours » <b>tout seul</b> — aucun acte n'est joué,
    /// rien n'a à être « repris ». C'est ce que l'ancienne pause demandait à un humain de faire.
    /// </summary>
    [Fact]
    public async Task The_same_rotation_is_not_suspended_once_the_window_has_passed()
    {
        await using var db = Seed(nameof(The_same_rotation_is_not_suspended_once_the_window_has_passed));
        Declare(db);
        await db.SaveChangesAsync();

        (await RowAsync(db, DuringExams)).SuspendedBy.Should().NotBeNull();
        (await RowAsync(db, AfterExams)).SuspendedBy.Should().BeNull();
    }

    [Fact]
    public async Task A_window_of_another_promotion_does_not_reach_this_one()
    {
        await using var db = Seed(nameof(A_window_of_another_promotion_does_not_reach_this_one));
        Declare(db, levelId: OtherLevelId);
        await db.SaveChangesAsync();

        (await RowAsync(db, DuringExams)).SuspendedBy.Should()
            .BeNull("deux promotions tournent dans les mêmes services le même matin");
    }

    /// <summary>
    /// ⚠ Une affectation qui n'a pas commencé n'est nulle part : l'annoncer « En examens » répondrait
    /// à une question que personne ne pose, et noierait les lignes où c'est vrai.
    /// </summary>
    [Fact]
    public async Task A_rotation_that_has_not_begun_is_not_suspended()
    {
        await using var db = Seed(nameof(A_rotation_that_has_not_begun_is_not_suspended), started: false);
        Declare(db);
        await db.SaveChangesAsync();

        var row = await RowAsync(db, DuringExams);

        row.Status.Should().Be(InternshipStatus.Planned);
        row.SuspendedBy.Should().BeNull();
    }

    /// <summary>
    /// ⚠ <b>La propriété que l'acte retiré ne savait pas offrir.</b> Révoquer la fenêtre éteint l'état
    /// partout à la lecture suivante, sans qu'aucune ligne ne soit réécrite — là où « reprendre »
    /// devait repasser sur chaque période, pouvait être oublié, et laissait des rotations gelées sans
    /// fin.
    /// </summary>
    [Fact]
    public async Task Revoking_the_window_clears_the_state_everywhere_without_writing_anything()
    {
        await using var db = Seed(nameof(Revoking_the_window_clears_the_state_everywhere_without_writing_anything));
        Declare(db);
        await db.SaveChangesAsync();

        (await RowAsync(db, DuringExams)).SuspendedBy.Should().NotBeNull();

        db.PromotionPauses.RemoveRange(db.PromotionPauses);
        await db.SaveChangesAsync();

        (await RowAsync(db, DuringExams)).SuspendedBy.Should().BeNull();

        // Et rien n'a été posé sur la rotation au passage : c'est tout l'intérêt.
        db.ServicePeriods.Should().OnlyContain(p => !p.IsPaused);
        db.ServicePeriods.Should().OnlyContain(p => p.Pauses.Count == 0);
    }

    /// <summary>
    /// ⚠ <c>IsPaused</c> et <c>SuspendedBy</c> sont deux faits différents et coexistent exprès : le
    /// premier est un drapeau <i>stocké</i> que plus aucun acte ne pose et qu'une annulation de
    /// téléversement peut encore remettre ; le second est dérivé du calendrier. Les confondre est
    /// l'erreur que le retrait de la pause par étape a servi à ne plus commettre.
    /// </summary>
    [Fact]
    public async Task A_stored_pause_and_a_declared_window_are_reported_apart()
    {
        await using var db = TestHarness.NewContext(nameof(A_stored_pause_and_a_declared_window_are_reported_apart));
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        db.SeedPausedPeriod(assignment, service,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 3),
            new DateOnly(2026, 3, 4), PauseKind.Other, "Remise en place par une annulation d'import");
        assignment.Start();
        await db.SaveChangesAsync();

        var row = await RowAsync(db, DuringExams);

        row.IsPaused.Should().BeTrue("le drapeau stocké est bien là");
        row.SuspendedBy.Should().BeNull("mais aucune fenêtre n'est déclarée pour cette promotion");
    }

    /// <summary>
    /// ⚠ <b>Le dossier de l'étudiant, qui est le chemin que son propre portail lit.</b> Rendue visible
    /// côté administration et muette ici, la suspension serait la même règle avec deux réponses selon
    /// qui regarde — et c'est l'étudiant qui a le plus besoin de savoir qu'il est attendu à un examen
    /// plutôt que dans un service.
    /// </summary>
    [Fact]
    public async Task The_students_own_file_carries_the_window_on_its_open_rotation()
    {
        await using var db = Seed(nameof(The_students_own_file_carries_the_window_on_its_open_rotation));
        Declare(db);
        await db.SaveChangesAsync();

        var assignment = db.InternshipAssignments.Single();
        var handler = new GetInternshipAssignmentByIdQueryHandler(
            db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(DuringExams));

        var result = await handler.Handle(
            new GetInternshipAssignmentByIdQuery(assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        var period = result.Value.ServicePeriods.Should().ContainSingle().Subject;
        period.SuspendedBy.Should().NotBeNull();
        period.SuspendedBy!.Reason.Should().Be("Examens du 1er semestre");
    }

    /// <summary>
    /// ⚠ <b>Et le dossier la porte aussi au niveau de l'affectation, pas seulement de ses périodes.</b>
    /// Signalé depuis l'écran le 23/09/2026 : « sur la rotation on voit En examens, sur le badge du
    /// stage on ne voit qu'En cours ». La ligne d'affectation de l'administration la portait depuis le
    /// début ; le dossier — que lit le portail étudiant — ne la posait que sur les périodes, donc le
    /// même écran se contredisait à une ligne d'intervalle, et celle du haut était la fausse.
    ///
    /// <para>⚠ Le critère est celui de l'<i>affectation</i> et non d'une période : l'étudiant compose
    /// quelles que soient les dates de tel séjour. C'est le même que
    /// <c>InternshipAssignmentSummaryResponse</c>, à dessein — une seule question, une seule réponse,
    /// quel que soit l'écran qui la pose.</para>
    /// </summary>
    [Fact]
    public async Task The_students_file_carries_the_window_on_the_stage_itself_not_only_its_rotations()
    {
        await using var db = Seed(
            nameof(The_students_file_carries_the_window_on_the_stage_itself_not_only_its_rotations));
        Declare(db);
        await db.SaveChangesAsync();

        var assignment = db.InternshipAssignments.Single();
        var handler = new GetInternshipAssignmentByIdQueryHandler(
            db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(DuringExams));

        var result = await handler.Handle(
            new GetInternshipAssignmentByIdQuery(assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SuspendedBy.Should().NotBeNull(
            "the stage badge and the rotation badge must not disagree on the same screen");
        result.Value.SuspendedBy!.Reason.Should().Be("Examens du 1er semestre");
    }

    /// <summary>
    /// ⚠ Le contrôle du précédent : hors fenêtre, le badge du stage ne dit rien non plus. Sans lui,
    /// un handler qui poserait la fenêtre sur toute affectation satisferait l'assertion ci-dessus.
    /// </summary>
    [Fact]
    public async Task The_stage_badge_says_nothing_once_the_window_has_passed()
    {
        await using var db = Seed(nameof(The_stage_badge_says_nothing_once_the_window_has_passed));
        Declare(db);
        await db.SaveChangesAsync();

        var assignment = db.InternshipAssignments.Single();
        var handler = new GetInternshipAssignmentByIdQueryHandler(
            db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(AfterExams));

        var result = await handler.Handle(
            new GetInternshipAssignmentByIdQuery(assignment.Id), default);

        result.Value.SuspendedBy.Should().BeNull();
    }

    /// <summary>
    /// ⚠ Le contrôle : hors fenêtre, le dossier ne dit rien de particulier — et la même lecture le
    /// prouve, plutôt qu'un second fichier qui pourrait diverger.
    /// </summary>
    [Fact]
    public async Task The_students_own_file_says_nothing_once_the_window_has_passed()
    {
        await using var db = Seed(nameof(The_students_own_file_says_nothing_once_the_window_has_passed));
        Declare(db);
        await db.SaveChangesAsync();

        var assignment = db.InternshipAssignments.Single();
        var handler = new GetInternshipAssignmentByIdQueryHandler(
            db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(AfterExams));

        var result = await handler.Handle(
            new GetInternshipAssignmentByIdQuery(assignment.Id), default);

        result.Value.ServicePeriods.Should().ContainSingle()
            .Which.SuspendedBy.Should().BeNull();
    }

    /// <summary>
    /// ⚠ <b>Seule la rotation ouverte porte la fenêtre, pas tout le dossier.</b> Un stage clos
    /// appartient au passé et un stage planifié n'a lieu nulle part : les marquer « En examens » ferait
    /// lire le parcours entier comme suspendu, ce qui est faux de deux lignes sur trois.
    /// </summary>
    [Fact]
    public async Task Only_the_open_rotation_of_the_file_carries_it()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_open_rotation_of_the_file_carries_it));
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        var closed = db.SeedPeriod(assignment, service,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 6), started: true, complete: true);
        var open = db.SeedPeriod(assignment, service,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 3), started: true);
        var planned = db.SeedPeriod(assignment, service,
            new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 5), started: false);

        assignment.Start();
        Declare(db);
        await db.SaveChangesAsync();

        var handler = new GetInternshipAssignmentByIdQueryHandler(
            db, new PromotionSuspensionLookup(db), TestHarness.ClockOn(DuringExams));

        var result = await handler.Handle(
            new GetInternshipAssignmentByIdQuery(assignment.Id), default);

        var byId = result.Value.ServicePeriods.ToDictionary(p => p.Id);
        byId[open.Id].SuspendedBy.Should().NotBeNull();
        byId[closed.Id].SuspendedBy.Should().BeNull("une rotation close appartient au passé");
        byId[planned.Id].SuspendedBy.Should().BeNull("une rotation planifiée n'a lieu nulle part");
    }
}
