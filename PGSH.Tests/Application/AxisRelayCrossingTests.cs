using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Une fois l'axe poussé, où mes étudiants tombent-ils sur une autre promotion ? »
///
/// <para>La question que rien ne posait avant le 20/09/2026. Le recalcul ne change pas <em>quel</em>
/// service une cohorte occupe, seulement <em>quand</em> — mais décaler des milliers de rotations les
/// fait arriver là où une autre promotion est déjà debout, et aucune lecture existante ne regardait
/// ce croisement : la page d'un service montre sa charge telle qu'elle est, la grille montre un plan
/// à la fois.</para>
///
/// <para>⚠ <b>C'est un rapport, jamais une garde.</b> Décision du 12/09/2026 : cette faculté dépasse
/// la capacité de ses services dans la plupart des cas. Ces tests vérifient donc ce qui est
/// <i>rapporté</i>, et l'un d'eux vérifie qu'aucun refus n'apparaît.</para>
/// </summary>
public class AxisRelayCrossingTests
{
    private const int SharedService = 60;
    private const int OtherLevel = 9;

    private static readonly DateOnly OursStart = new(2026, 1, 5),  OursEnd = new(2026, 1, 16);
    private static readonly DateOnly TheirsStart = new(2026, 1, 19), TheirsEnd = new(2026, 1, 30);

    /// <summary>
    /// Notre P1 dans un service, et une autre promotion dans le <b>même</b> service juste après.
    /// Tant que les deux fenêtres ne se touchent pas, personne ne se croise.
    /// </summary>
    private static ApplicationDbContext Seed(
        string name, int ourStudents, int theirStudents,
        DateOnly? ourStart = null, DateOnly? ourEnd = null)
    {
        var db = TestHarness.NewContext(name);

        var ours = db.SeedCatalog();
        var service = db.SeedService(SharedService, "Cardiologie A");

        var ourCohort = db.SeedCohort(ours, groupId: 1, groupLabel: "G1");
        var ourSlot = db.SeedSlot(ours, 8101, 1, ourStart ?? OursStart, ourEnd ?? OursEnd);
        db.SeedSlotAssignment(8201, ourCohort, ourSlot, service);

        for (int i = 0; i < ourStudents; i++)
            db.SeedAssignment(db.SeedRegistration($"Notre{i}", "Etudiant"), ourCohort);

        // Une autre promotion, dans le même service, sur la fenêtre d'après.
        db.SeedLevel(OtherLevel, "Sixième Année Médecine", year: 6);
        var theirStage = db.SeedStage(900, "Chirurgie", levelId: OtherLevel);
        var theirCohort = db.SeedCohort(theirStage, groupId: 2, groupLabel: "G2");
        var theirSlot = db.SeedSlot(theirStage, 8102, 1, TheirsStart, TheirsEnd);
        db.SeedSlotAssignment(8202, theirCohort, theirSlot, service);

        for (int i = 0; i < theirStudents; i++)
            db.SeedAssignment(db.SeedRegistration($"Autre{i}", "Etudiant"), theirCohort);

        db.SaveChanges();
        return db;
    }

    private static Task<AxisRelayCrossings> ReadAsync(
        ApplicationDbContext db, DateOnly newStart, DateOnly newEnd) =>
        new AxisRelayCrossingReader(db).ReadAsync(
            TestHarness.LevelId,
            new Dictionary<int, (DateOnly, DateOnly)> { [1] = (newStart, newEnd) },
            default);

    /// <summary>
    /// ⚠ <b>Le contrôle.</b> Déplacer notre colonne dans une fenêtre encore libre ne croise
    /// personne. Sans lui, un lecteur qui signalerait tout satisferait le test suivant.
    /// </summary>
    [Fact]
    public async Task Moving_into_an_empty_window_crosses_nobody()
    {
        await using var db = Seed(nameof(Moving_into_an_empty_window_crosses_nobody), 10, 8);

        var report = await ReadAsync(db, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 13));

        report.ServicesExamined.Should().Be(1);
        report.ServicesWherePeakRises.Should().Be(0);
        report.Listed.Should().BeEmpty();
    }

    /// <summary>
    /// Le cas pour lequel le rapport existe : poussée sur la fenêtre de l'autre promotion, notre
    /// cohorte s'y ajoute et le pic du service monte.
    /// </summary>
    [Fact]
    public async Task Pushing_onto_another_promotion_raises_the_peak_and_names_it()
    {
        await using var db = Seed(nameof(Pushing_onto_another_promotion_raises_the_peak_and_names_it), 10, 8);

        var report = await ReadAsync(db, TheirsStart, TheirsEnd);

        report.ServicesWherePeakRises.Should().Be(1);

        var crossing = report.Listed.Single();
        crossing.ServiceId.Should().Be(SharedService);
        crossing.PeakBefore.Should().Be(10, "before the move, the busiest moment was our own ten");
        crossing.PeakAfter.Should().Be(18, "now both cohorts stand there at once");
        crossing.Increase.Should().Be(8);
        crossing.OtherPromotions.Should().ContainSingle(
            "the whole point is naming who else is there");
    }

    /// <summary>
    /// ⚠ <b>Le pic, jamais la somme</b> — le défaut mesuré le 03/09/2026, qui affichait 118 sur un
    /// service n'ayant jamais porté plus de 62. Deux fenêtres consécutives qui ne se touchent pas
    /// <i>ne s'additionnent pas</i>, même si toutes deux tombent dans l'étendue examinée.
    /// </summary>
    [Fact]
    public async Task Two_consecutive_windows_are_not_added_together()
    {
        await using var db = Seed(nameof(Two_consecutive_windows_are_not_added_together), 10, 8);

        // On se déplace d'un seul jour : toujours pas de chevauchement avec l'autre promotion.
        var report = await ReadAsync(db, OursStart.AddDays(1), OursEnd.AddDays(1));

        report.ServicesWherePeakRises.Should().Be(0,
            "10 and 8 that never share a day are 10, not 18");
    }

    /// <summary>
    /// ⚠ Un chevauchement <b>partiel</b> compte quand même : le pic vit dans l'intersection, si
    /// courte soit-elle.
    /// </summary>
    [Fact]
    public async Task A_partial_overlap_is_reported_with_the_days_it_covers()
    {
        await using var db = Seed(nameof(A_partial_overlap_is_reported_with_the_days_it_covers), 10, 8);

        // Notre fenêtre mord sur les trois premiers jours de la leur.
        var report = await ReadAsync(db, new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 21));

        var crossing = report.Listed.Single();
        crossing.PeakAfter.Should().Be(18);
        crossing.PeakStart.Should().Be(TheirsStart, "the peak begins when the second cohort arrives");
        crossing.PeakEnd.Should().Be(new DateOnly(2026, 1, 21), "and ends when the first leaves");
    }

    /// <summary>
    /// ⚠ Le rapport ne refuse rien : il n'y a pas de résultat en échec à obtenir ici, quelle que
    /// soit l'ampleur du dépassement. Le dépassement est le fonctionnement de cette faculté
    /// (décision du 12/09/2026), et une garde de capacité nouvelle serait une régression.
    /// </summary>
    [Fact]
    public async Task A_large_overlap_is_still_only_a_report()
    {
        await using var db = Seed(nameof(A_large_overlap_is_still_only_a_report), 200, 300);

        var report = await ReadAsync(db, TheirsStart, TheirsEnd);

        report.Listed.Single().PeakAfter.Should().Be(500);
        report.ServicesWherePeakRises.Should().Be(1);
    }

    /// <summary>
    /// ⚠ <b>La moitié qu'un compte de pics seul ne dit pas — et qui a failli manquer.</b> Mesuré sur
    /// la base vivante le 20/09/2026 : pousser la 4ᵉ MED de cinq jours ne fait monter le pic
    /// d'<i>aucun</i> de ses 23 services, parce qu'en Dermatologie la colonne de la 4ᵉ chevauchait
    /// déjà celles de la 3ᵉ et que l'allonger ne fait que prolonger la même coïncidence. Un rapport
    /// qui n'aurait annoncé que « 0 service plus chargé » se serait lu « rien ne change ».
    /// </summary>
    [Fact]
    public async Task A_service_that_stays_busy_longer_is_reported_even_when_the_peak_holds()
    {
        // ⚠ Les deux fenêtres se chevauchent **déjà** de quatre jours : c'est ce qui rend le cas
        // intéressant. Sans chevauchement préalable, allonger crée un pic et le test ne mesurerait
        // que la hausse — ce que le cas précédent couvre déjà.
        var overlapStart = new DateOnly(2026, 1, 14);
        await using var db = Seed(
            nameof(A_service_that_stays_busy_longer_is_reported_even_when_the_peak_holds), 10, 8,
            ourStart: overlapStart, ourEnd: new DateOnly(2026, 1, 22));

        // On allonge la nôtre sans bouger son début : la charge simultanée reste 18, elle dure
        // simplement plus longtemps.
        var report = await ReadAsync(db, overlapStart, new DateOnly(2026, 1, 28));

        report.ServicesWherePeakRises.Should().Be(0, "18 was already reached before");
        report.ServicesWhereBusyLasts.Should().Be(1);

        var crossing = report.Listed.Single();
        crossing.Increase.Should().Be(0);
        crossing.StaysBusyLonger.Should().BeTrue();
        crossing.BusiestDaysAfter.Should().BeGreaterThan(crossing.BusiestDaysBefore);
    }

    /// <summary>
    /// ⚠ Le contrôle de la précédente : une colonne qui ne touche toujours personne n'apparaît dans
    /// aucun des deux comptes. Sans lui, un lecteur qui signalerait tout la satisferait.
    /// </summary>
    [Fact]
    public async Task A_move_that_changes_neither_height_nor_duration_is_silent()
    {
        await using var db = Seed(
            nameof(A_move_that_changes_neither_height_nor_duration_is_silent), 10, 8);

        var report = await ReadAsync(db, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 13));

        report.ServicesWherePeakRises.Should().Be(0);
        report.ServicesWhereBusyLasts.Should().Be(0);
        report.Listed.Should().BeEmpty();
    }

    /// <summary>Rien à déplacer, rien à examiner — et surtout pas une requête pour le découvrir.</summary>
    [Fact]
    public async Task No_moved_column_examines_nothing()
    {
        await using var db = Seed(nameof(No_moved_column_examines_nothing), 10, 8);

        var report = await new AxisRelayCrossingReader(db)
            .ReadAsync(TestHarness.LevelId, new Dictionary<int, (DateOnly, DateOnly)>(), default);

        report.ServicesExamined.Should().Be(0);
        report.Listed.Should().BeEmpty();
    }
}
