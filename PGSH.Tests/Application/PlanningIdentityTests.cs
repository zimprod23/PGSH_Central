using FluentAssertions;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// L'identité des trois racines de la planification — le créneau, la cohorte, le groupe — est-elle
/// <b>exigée</b>, ou laissée à la mémoire de l'appelant&nbsp;?
/// </summary>
/// <remarks>
/// <para>⚠ <b>C'est la classe de défaut que <c>CLAUDE.md</c> décrit comme celle où « tout bug de cette
/// famille vit » : une clé invariante d'année (<c>stageId</c>) employée pour atteindre des lignes que
/// l'année constitue.</b> Un <c>StageSlot</c> est clé <c>(StageId, AcademicYearId, PeriodNumber)</c> —
/// le même P1 existe une fois par promotion, avec ses propres dates. La règle était écrite dans
/// <c>CLAUDE.md</c>, tenue par un index unique dans PostgreSQL, et <b>garantie nulle part dans le
/// code</b> : trois endroits construisaient un créneau par initialiseur d'objet, deux stampaient
/// l'année, le troisième l'oubliait — il écrivait un créneau d'année <c>0</c>.</para>
///
/// <para>Un initialiseur d'objet ne peut pas exiger un champ ; un constructeur, si. Depuis
/// <c>StageSlot.For</c>, l'oubli n'est plus un bug à trouver en relisant : c'est une <b>erreur de
/// compilation</b>. La preuve en est que le changement a lui-même désigné les quatre coupables — les
/// trois handlers, plus une fixture de test qui posait un créneau sans année, donc un objet qu'aucun
/// chemin réel ne pouvait produire.</para>
///
/// <para>⚠ <b>Ces cas ne remplacent pas le compilateur, ils le complètent.</b> Le compilateur interdit
/// d'<em>omettre</em> l'année ; ces cas interdisent d'en passer une qui n'en est pas une (0, négative),
/// ce qu'un <c>int</c> laisse écrire.</para>
///
/// <para>Le même traitement a été donné le 14/09/2026 à <see cref="Cohort"/> — identité
/// <c>(StageId, AcademicGroupId)</c>, cinq sites de construction — et à <see cref="AcademicGroup"/> —
/// identité <c>(AcademicYearId, LevelId, GroupNumber)</c>, trois sites. Là encore le changement a
/// nommé ses propres coupables : une fixture d'<c>AbolishedStageRevalidationTests</c> omettait le
/// niveau de son groupe, donc posait « Non réparti » sans le vouloir et bâtissait dessus une cohorte
/// de rattrapage — exactement l'état que <c>CreateCohortCommandHandler</c> refuse.</para>
/// </remarks>
public class PlanningIdentityTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End = new(2026, 3, 27);

    [Fact]
    public void A_slot_needs_an_academic_year()
    {
        var made = StageSlot.For(stageId: 1, academicYearId: 0, periodNumber: 1, Start, End);

        made.IsFailure.Should().BeTrue(
            "zéro est la valeur par défaut d'un int, donc l'oubli et « l'année 0 » sont le même geste");
        made.Error.Code.Should().Be("Schedule.SlotNeedsAcademicYear");
    }

    [Fact]
    public void A_slot_needs_a_stage()
    {
        StageSlot.For(stageId: 0, academicYearId: 1, periodNumber: 1, Start, End)
            .Error.Code.Should().Be("Schedule.SlotNeedsStage");
    }

    [Fact]
    public void A_slot_needs_a_period_number()
    {
        StageSlot.For(stageId: 1, academicYearId: 1, periodNumber: 0, Start, End)
            .Error.Code.Should().Be("Schedule.SlotNeedsPeriodNumber");
    }

    /// <summary>
    /// ⚠ Une fenêtre négative est <b>silencieuse</b> : chaque calcul de durée en aval la lit comme un
    /// nombre de jours négatif et rend des totaux que rien n'annonce comme absurdes.
    /// </summary>
    [Fact]
    public void A_slot_cannot_end_before_it_starts()
    {
        StageSlot.For(stageId: 1, academicYearId: 1, periodNumber: 1, End, Start)
            .Error.Code.Should().Be("Schedule.PeriodWindowReversed");
    }

    /// <summary>
    /// ⚠ <b>Le témoin.</b> Sans lui, tout ce qui précède passerait aussi bien si la fabrique refusait
    /// systématiquement — et l'identité complète doit rester lisible sur l'objet produit.
    /// </summary>
    [Fact]
    public void A_slot_with_its_full_identity_is_made_and_keeps_it()
    {
        var made = StageSlot.For(stageId: 7, academicYearId: 3, periodNumber: 2, Start, End, "P2");

        made.IsSuccess.Should().BeTrue();
        made.Value.StageId.Should().Be(7);
        made.Value.AcademicYearId.Should().Be(3);
        made.Value.PeriodNumber.Should().Be(2);
        made.Value.Label.Should().Be("P2");
    }

    // ─── Cohort — (StageId, AcademicGroupId) ──────────────────────────────────

    /// <summary>
    /// ⚠ <b>Aucun index unique ne tient cette paire</b>, contrairement au créneau : la fabrique ne
    /// peut donc rien contre le doublon, que <c>CreateCohortCommandHandler</c> et
    /// <c>CohortProvisioner</c> cherchent eux-mêmes. Ce qu'elle tient est l'autre moitié — une
    /// cohorte amputée d'un de ses deux côtés, c'est-à-dire une ligne qu'aucune lecture ne sait
    /// interpréter.
    /// </summary>
    [Fact]
    public void A_cohort_needs_a_stage()
    {
        Cohort.For(stageId: 0, academicGroupId: 4, "G4")
            .Error.Code.Should().Be("Cohorts.CohortNeedsStage");
    }

    [Fact]
    public void A_cohort_needs_a_roster()
    {
        Cohort.For(stageId: 3, academicGroupId: 0, "G4")
            .Error.Code.Should().Be("Cohorts.CohortNeedsRoster");
    }

    /// <summary>
    /// ⚠ Le négatif n'est pas le zéro : le zéro est l'oubli, un identifiant négatif est une valeur
    /// qu'un <c>int</c> laisse écrire et qu'aucune clé ne porte. Il faut les deux cas — un test du
    /// seul zéro passerait avec une garde écrite <c>== 0</c>.
    /// </summary>
    [Fact]
    public void A_cohort_refuses_a_negative_key()
    {
        Cohort.For(stageId: -1, academicGroupId: 4, "G4").IsFailure.Should().BeTrue();
        Cohort.For(stageId: 3, academicGroupId: -1, "G4").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_cohort_with_its_full_identity_is_made_and_keeps_it()
    {
        var made = Cohort.For(stageId: 3, academicGroupId: 4, "Cardiologie · G4");

        made.IsSuccess.Should().BeTrue();
        made.Value.StageId.Should().Be(3);
        made.Value.AcademicGroupId.Should().Be(4);
        made.Value.Label.Should().Be("Cardiologie · G4");
    }

    // ─── AcademicGroup — (AcademicYearId, LevelId, GroupNumber) ───────────────

    [Fact]
    public void A_roster_needs_an_academic_year()
    {
        AcademicGroup.ForPromotion(academicYearId: 0, levelId: 3, groupNumber: 1, "G1")
            .Error.Code.Should().Be("AcademicGroups.RosterNeedsAcademicYear");
    }

    /// <summary>
    /// ⚠ <b>Le cœur de la paire de fabriques.</b> Sous une fabrique unique à niveau nullable, oublier
    /// la promotion rendait « Non réparti » — le panier qui rassemble les inscriptions non réparties
    /// de <em>toutes</em> les promotions de l'année, 4 725 en 2025-2026. Ici l'oubli est refusé, et
    /// le panier se demande : voir <see cref="The_unassigned_bucket_is_asked_for_deliberately"/>.
    /// </summary>
    [Fact]
    public void A_promotion_roster_needs_its_promotion()
    {
        AcademicGroup.ForPromotion(academicYearId: 2, levelId: 0, groupNumber: 1, "G1")
            .Error.Code.Should().Be("AcademicGroups.RosterNeedsPromotion");
    }

    /// <summary>
    /// ⚠ Le zéro est refusé <em>parce qu'</em>il désigne le panier : un groupe de promotion numéroté
    /// 0 serait indiscernable de « Non réparti » sur le seul numéro, et la numérotation d'une
    /// promotion part de 1 — c'est ce que <c>IX_AcademicGroup_Year_Level_Number</c> compte.
    /// </summary>
    [Fact]
    public void A_promotion_roster_is_numbered_from_one()
    {
        AcademicGroup.ForPromotion(academicYearId: 2, levelId: 3, groupNumber: 0, "G0")
            .Error.Code.Should().Be("AcademicGroups.RosterNeedsNumber");
    }

    [Fact]
    public void A_roster_refuses_a_negative_key()
    {
        AcademicGroup.ForPromotion(-1, levelId: 3, groupNumber: 1, "G1").IsFailure.Should().BeTrue();
        AcademicGroup.ForPromotion(2, levelId: -1, groupNumber: 1, "G1").IsFailure.Should().BeTrue();
        AcademicGroup.ForPromotion(2, levelId: 3, groupNumber: -1, "G1").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_promotion_roster_with_its_full_identity_is_made_and_keeps_it()
    {
        var made = AcademicGroup.ForPromotion(
            academicYearId: 2, levelId: 3, groupNumber: 7, "Groupe 7 — 3ème année",
            rotationGroup: "B");

        made.IsSuccess.Should().BeTrue();
        made.Value.AcademicYearId.Should().Be(2);
        made.Value.LevelId.Should().Be(3);
        made.Value.GroupNumber.Should().Be(7);
        made.Value.RotationGroup.Should().Be("B");
    }

    /// <summary>
    /// ⚠ <b>Le panier est légitime, et c'est pour cela qu'il a sa propre porte.</b> Il sort sans
    /// niveau et hors numérotation — ce que la fabrique de promotion refuse justement de produire.
    /// </summary>
    [Fact]
    public void The_unassigned_bucket_is_asked_for_deliberately()
    {
        var made = AcademicGroup.AsUnassignedBucket(academicYearId: 2, "Non réparti");

        made.IsSuccess.Should().BeTrue();
        made.Value.LevelId.Should().BeNull();
        made.Value.GroupNumber.Should().Be(
            0, "le panier n'est pas le groupe zéro de l'année, il est hors de la numérotation");
    }

    /// <summary>
    /// ⚠ L'année, elle, ne lui manque jamais : un panier sans année vaudrait pour toutes les
    /// promotions de toutes les années à la fois.
    /// </summary>
    [Fact]
    public void Even_the_bucket_needs_an_academic_year()
    {
        AcademicGroup.AsUnassignedBucket(academicYearId: 0, "Non réparti")
            .Error.Code.Should().Be("AcademicGroups.RosterNeedsAcademicYear");
    }

    /// <summary>
    /// ⚠ <b>Le témoin de la distinction.</b> Sans lui, les deux fabriques pourraient rendre la même
    /// chose et tous les cas ci-dessus passeraient encore — or c'est précisément que le panier et un
    /// groupe de promotion ne se confondent pas qui est en jeu.
    /// </summary>
    [Fact]
    public void The_two_roster_shapes_are_not_the_same_object()
    {
        var roster = AcademicGroup.ForPromotion(2, levelId: 3, groupNumber: 1, "G1").Value;
        var bucket = AcademicGroup.AsUnassignedBucket(2, "Non réparti").Value;

        roster.LevelId.Should().NotBeNull();
        bucket.LevelId.Should().BeNull();
        roster.GroupNumber.Should().NotBe(bucket.GroupNumber);
    }

    /// <summary>
    /// ⚠ <b>Une étiquette de partition sur le panier n'est pas refusée ici, elle est impossible.</b>
    /// C'est l'acte qui a donné une cohorte à 4 725 personnes : <c>CohortProvisioner</c> lit les
    /// groupes par étiquette, et le panier en portant une y entre en entier. La fabrique du panier
    /// ne prend tout simplement pas le paramètre.
    /// </summary>
    [Fact]
    public void The_bucket_leaves_the_factory_without_a_partition_label()
    {
        AcademicGroup.AsUnassignedBucket(2, "Non réparti").Value
            .RotationGroup.Should().BeNull();
    }
}
