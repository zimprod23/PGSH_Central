using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.Manage;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Découper cette promotion en <b>N</b> groupes » — la seconde façon de nommer une coupe, à travers
/// le handler.
///
/// <para>La faculté pense dans cette unité aussi souvent que dans l'autre (« la 5ᵉ MED en 100
/// groupes »), et jusqu'ici seule la taille pouvait être demandée. <c>RosterCutTests</c> couvre
/// l'arithmétique&nbsp;; ce fichier couvre ce que le handler en fait — l'apport entre textes CNPN, le
/// refus, et le fait que les signalements restent écartés nommément.</para>
/// </summary>
public class RosterCutByCountTests
{
    private static async Task<ApplicationDbContext> SeedPromotionAsync(string name, int students)
    {
        var db = TestHarness.NewContext(name);
        db.SeedCatalog();

        for (int i = 1; i <= students; i++)
            db.SeedRegistration($"E{i:D3}", "Test");

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<List<int>> GroupSizesAsync(ApplicationDbContext db)
    {
        var placed = await db.Registrations
            .Where(r => r.AcademicGroupId != null)
            .Select(r => r.AcademicGroupId!.Value)
            .ToListAsync();

        return [.. placed.GroupBy(id => id).Select(g => g.Count()).OrderByDescending(n => n)];
    }

    /// <summary>The act the user asked for, end to end.</summary>
    [Fact]
    public async Task A_promotion_is_cut_into_the_number_of_groups_asked_for()
    {
        await using var db = await SeedPromotionAsync(nameof(A_promotion_is_cut_into_the_number_of_groups_asked_for), 232);

        var result = await new AutoArrangeGroupsCommandHandler(db, new RecordingAuditTrail()).Handle(
            new AutoArrangeGroupsCommand(
                TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: null, GroupCount: 12),
            default);

        result.IsSuccess.Should().BeTrue();

        var sizes = await GroupSizesAsync(db);
        sizes.Should().HaveCount(12);
        sizes.Sum().Should().Be(232);
        (sizes.Max() - sizes.Min()).Should().Be(1, "également, à un étudiant près");
    }

    /// <summary>
    /// ⚠ <b>Le défaut mesuré à l'écran le 10/09/2026</b>, sur le chemin par taille — celui qui était
    /// déjà là. Le nombre de groupes ne change pas ; ce qui disparaît est le groupe de 12 posé à côté
    /// de onze groupes de 20, qui partait ensuite en rotation comme une cohorte entière.
    /// </summary>
    [Fact]
    public async Task Cutting_by_size_no_longer_leaves_a_runt_group()
    {
        await using var db = await SeedPromotionAsync(nameof(Cutting_by_size_no_longer_leaves_a_runt_group), 232);

        var result = await new AutoArrangeGroupsCommandHandler(db, new RecordingAuditTrail()).Handle(
            new AutoArrangeGroupsCommand(TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: 20),
            default);

        result.IsSuccess.Should().BeTrue();

        var sizes = await GroupSizesAsync(db);
        sizes.Should().HaveCount(12, "le nombre de groupes est celui qu'il a toujours été");
        sizes.Should().NotContain(12, "c'est exactement le groupe qui ne doit plus exister");
        sizes.Min().Should().Be(19);
        sizes.Max().Should().Be(20, "« taille de groupe » est un maximum, et il tient");
    }

    /// <summary>
    /// ⚠ <b>Plus de groupes que d'étudiants : refusé, et le refus nomme les deux nombres.</b> Couper
    /// silencieusement en moins laisserait l'opérateur croire que la promotion a une forme qu'elle
    /// n'a pas ; un refus qui ne nommerait qu'un seul chiffre le renverrait deviner lequel il a mal lu.
    /// </summary>
    [Fact]
    public async Task More_groups_than_students_is_refused_by_naming_both_numbers()
    {
        await using var db = await SeedPromotionAsync(nameof(More_groups_than_students_is_refused_by_naming_both_numbers), 5);

        var result = await new AutoArrangeGroupsCommandHandler(db, new RecordingAuditTrail()).Handle(
            new AutoArrangeGroupsCommand(
                TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: null, GroupCount: 40),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.MoreGroupsThanStudents");
        result.Error.Description.Should().Contain("40").And.Contain("5");

        (await db.AcademicGroups.CountAsync()).Should().Be(0, "un acte refusé n'écrit rien");
    }

    /// <summary>
    /// ⚠ <b>Les paniers CNPN cassent la division, et c'est le cœur de l'affaire.</b> Les groupes ne
    /// mélangent jamais deux textes, donc « N » s'apporte entre eux avant qu'on ne coupe — et chaque
    /// texte garde des groupes entiers à lui.
    /// </summary>
    [Fact]
    public async Task A_count_is_apportioned_between_the_texts_and_never_mixes_them()
    {
        await using var db = TestHarness.NewContext(nameof(A_count_is_apportioned_between_the_texts_and_never_mixes_them));
        db.SeedCatalog();

        // The stamp lives on the student, and the handler reads the registration's first — the same
        // route CnpnPlanningTests takes.
        foreach (int i in Enumerable.Range(1, 60))
            db.SeedRegistration($"N{i:D3}", "Test").Student.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        foreach (int i in Enumerable.Range(1, 30))
            db.SeedRegistration($"O{i:D3}", "Test").Student.AssignCnpnVersion(TestHarness.OldCnpnId, isInferred: false);

        await db.SaveChangesAsync();

        var result = await new AutoArrangeGroupsCommandHandler(db, new RecordingAuditTrail()).Handle(
            new AutoArrangeGroupsCommand(
                TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: null, GroupCount: 9),
            default);

        result.IsSuccess.Should().BeTrue();

        var byGroup = await db.Registrations
            .Include(r => r.Student)
            .Where(r => r.AcademicGroupId != null)
            .Select(r => new { r.AcademicGroupId, Text = r.Student.CnpnVersionId })
            .ToListAsync();

        byGroup.GroupBy(r => r.AcademicGroupId!.Value)
            .Should().OnlyContain(g => g.Select(r => r.Text).Distinct().Count() == 1,
                "un groupe tourne ensemble sur un jeu de stages — deux textes n'y tiennent pas");

        byGroup.Select(r => r.AcademicGroupId).Distinct().Should().HaveCount(9,
            "60 et 30 se partagent 9 groupes exactement : 6 et 3");
    }

    /// <summary>
    /// Les inscriptions gelées restent écartées nommément — le comportement existait, et il ne doit
    /// pas se perdre dans la branche neuve.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Le motif compte.</b> Seuls <c>OutstandingPriorStages</c> et
    /// <c>AbsentFromReinscriptionRoll</c> retirent une inscription de la planification ; un dossier
    /// incomplet est <i>consultatif</i> — il paraît dans la liste de travail et se répartit quand
    /// même. Un test écrit avec un motif consultatif passerait en n'observant rien.
    /// </remarks>
    [Fact]
    public async Task Held_registrations_are_still_refused_by_name_on_the_count_path()
    {
        await using var db = await SeedPromotionAsync(nameof(Held_registrations_are_still_refused_by_name_on_the_count_path), 10);

        // ⚠ Through the aggregate, never by building the child here: an entity given a key and added
        // to an already-tracked parent is classified Modified, and the save dies on 0 rows. CLAUDE.md,
        // « Store-generated keys ».
        var held = await db.Registrations.FirstAsync();
        held.PlaceOnHold(
            RegistrationHoldReason.AbsentFromReinscriptionRoll,
            "absent du rouleau de réinscription",
            new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await new AutoArrangeGroupsCommandHandler(db, new RecordingAuditTrail()).Handle(
            new AutoArrangeGroupsCommand(
                TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: null, GroupCount: 3),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.FailureCount.Should().Be(1, "le signalement est rapporté, jamais avalé");
        (await GroupSizesAsync(db)).Sum().Should().Be(9, "les neuf autres sont répartis");
    }
}
