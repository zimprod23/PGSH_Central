using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// L'arithmétique du rattrapage : reposer les colonnes d'un axe sur le calendrier de sa promotion,
/// une fois qu'une fenêtre y a été déclarée trop tard.
///
/// <para>Pure — ni base, ni horloge — donc les cas pénibles s'éprouvent directement plutôt qu'à
/// travers une fixture de planification : une fenêtre qui coupe la colonne en cours, une colonne
/// ancrée au milieu, un axe qui déborde. Même raison que <c>RotationCyclePlannerTests</c>.</para>
/// </summary>
public class AxisRelayPlannerTests
{
    private const int ColumnLength = 10;   // jours ouvrables

    /// <summary>Lundi 5 janvier 2026.</summary>
    private static readonly DateOnly Anchor = new(2026, 1, 5);

    private static WorkingDayCalendar Weekends() => WorkingDayCalendar.WeekendsOnly();

    private static WorkingDayCalendar WithWindow(DateOnly from, DateOnly to) =>
        WorkingDayCalendar.Build([new PromotionPause
        {
            Id = 1, AcademicYearId = 22, LevelId = 4,
            StartDate = from, EndDate = to,
            Kind = PauseKind.Exam, Reason = "Examens du 1er semestre",
        }]);

    /// <summary>L'axe tel qu'il a été posé : <paramref name="count"/> colonnes bout à bout.</summary>
    private static List<AxisColumn> Axis(int count, WorkingDayCalendar? laidOn = null)
    {
        var calendar = laidOn ?? Weekends();
        return calendar.LaySeries(Anchor, count, ColumnLength)
            .Select((w, i) => new AxisColumn(i + 1, w.Start, w.End, IsMovedByHand: false))
            .ToList();
    }

    private static AxisRelayPlan PlanOf(
        List<AxisColumn> axis, WorkingDayCalendar calendar, int from = 1)
    {
        var result = AxisRelayPlanner.Plan(axis, calendar, ColumnLength, from);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");
        return result.Value;
    }

    // ─── Le contrôle ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>Le contrôle sans lequel rien d'autre ne prouve quoi que ce soit.</b> Reposer un axe sur
    /// le calendrier qui l'a produit ne doit rien déplacer. Un planificateur qui décale tout d'un
    /// jour satisferait tous les tests « ça a bougé » qui suivent.
    /// </summary>
    [Fact]
    public void Relaying_an_axis_on_the_calendar_that_laid_it_moves_nothing()
    {
        var calendar = Weekends();
        var axis = Axis(8, calendar);

        var plan = PlanOf(axis, calendar);

        plan.ColumnsMoved.Should().Be(0);
        plan.WorkingDaysRecovered.Should().Be(0);
        plan.Columns.Should().OnlyContain(c => !c.Moved);
    }

    /// <summary>
    /// ⚠ Une fenêtre posée sur un week-end ne prend aucun jour ouvrable : proposer un rattrapage
    /// serait proposer un acte sans effet, et un avertissement qui se déclenche pour rien se fait
    /// ignorer — ce qui met le vrai hors de vue.
    /// </summary>
    [Fact]
    public void A_window_over_a_weekend_shortens_no_column()
    {
        var axis = Axis(8);
        // Samedi 10 et dimanche 11 janvier 2026.
        var calendar = WithWindow(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 11));

        AxisRelayPlanner.FirstShortColumn(axis, calendar, ColumnLength).Should().BeNull();
    }

    // ─── Le cas pour lequel l'acte existe ───────────────────────────────────────

    /// <summary>
    /// ⚠ <b>La colonne coupée garde son début et voit sa fin repoussée.</b> C'est toute la
    /// différence avec « reposer l'axe depuis le départ » : les étudiants sont entrés dans cette
    /// colonne à cette date-là, et la déplacer réécrirait ce qui a eu lieu.
    /// </summary>
    [Fact]
    public void The_cut_column_keeps_its_start_and_is_extended()
    {
        var axis = Axis(4);
        var cut = axis[0];

        // Une semaine pleine d'examens à l'intérieur de la première colonne.
        var calendar = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        var plan = PlanOf(axis, calendar);

        var first = plan.Columns[0];
        first.ToStart.Should().Be(cut.StartDate, "somebody already walked in on that date");
        first.ToEnd.Should().BeAfter(cut.EndDate);
        first.ToWorkingDays.Should().Be(ColumnLength, "a relaid column always holds its full length");
        first.FromWorkingDays.Should().Be(ColumnLength - 5, "five worked days were taken");
    }

    /// <summary>
    /// ⚠ <b>La cascade</b> — la pièce qui manquait au déplacement colonne par colonne. Pousser P1
    /// sans pousser P2 laisse deux colonnes qui se chevauchent.
    /// </summary>
    [Fact]
    public void Every_later_column_follows_the_one_before_it()
    {
        var axis = Axis(4);
        var calendar = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));

        var plan = PlanOf(axis, calendar);

        plan.ColumnsMoved.Should().Be(4);
        plan.WorkingDaysRecovered.Should().Be(5);

        for (int i = 1; i < plan.Columns.Count; i++)
            plan.Columns[i].ToStart.Should().BeAfter(plan.Columns[i - 1].ToEnd,
                "a column begins after the one before it ends");

        plan.Columns.Should().OnlyContain(c => c.ToWorkingDays == ColumnLength);
    }

    /// <summary>
    /// Les colonnes <em>antérieures</em> à celle demandée ne sont pas touchées : elles sont derrière
    /// nous, et rien de ce qui a été servi ne se réécrit.
    /// </summary>
    [Fact]
    public void Columns_before_the_starting_one_are_left_alone()
    {
        var axis = Axis(4);
        var calendar = WithWindow(new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 13));

        var plan = PlanOf(axis, calendar, from: 3);

        plan.Columns.Should().HaveCount(2);
        plan.Columns.Select(c => c.Number).Should().Equal(3, 4);
    }

    // ─── L'ancre ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Une colonne déplacée à la main garde ses dates, et la cascade reprend <b>après</b> elle.
    /// </summary>
    [Fact]
    public void A_hand_moved_column_anchors_and_the_cascade_resumes_after_it()
    {
        var axis = Axis(4);
        // P4 poussée loin à la main : elle ancre, et il reste de la place avant elle.
        axis[3] = axis[3] with
        {
            StartDate = new DateOnly(2026, 4, 6),
            EndDate = new DateOnly(2026, 4, 17),
            IsMovedByHand = true,
        };

        var calendar = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        var plan = PlanOf(axis, calendar);

        var anchored = plan.Columns.Single(c => c.Number == 4);
        anchored.Anchored.Should().BeTrue();
        anchored.Moved.Should().BeFalse();
        anchored.ToStart.Should().Be(new DateOnly(2026, 4, 6), "a human put it there");

        plan.ColumnsAnchored.Should().Be(1);
        plan.ColumnsMoved.Should().Be(3);
    }

    /// <summary>
    /// ⚠ <b>Le refus qui vaut mieux qu'un ordre cassé en silence.</b> Si la cascade vient mordre sur
    /// une ancre, ni la pousser (ce serait effacer la décision) ni l'enjamber (ce serait un axe
    /// incohérent) n'est acceptable — l'acte s'arrête et nomme les deux colonnes.
    /// </summary>
    [Fact]
    public void The_cascade_refuses_rather_than_running_into_an_anchor()
    {
        var axis = Axis(4);
        axis[2] = axis[2] with { IsMovedByHand = true };   // P3 reste où elle est

        // Deux semaines perdues en P1 : P2 déborde nécessairement sur P3.
        var calendar = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 23));

        var result = AxisRelayPlanner.Plan(axis, calendar, ColumnLength, fromColumn: 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.RelayOverlapsAnchoredColumn");
        result.Error.Description.Should().Contain("P3");
    }

    // ─── Rejouabilité ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>La propriété qui rend l'acte sûr : il est idempotent.</b> Un recalcul est relancé après
    /// chaque fenêtre déclarée, corrigée ou révoquée. S'il ajoutait à ce qui est stocké au lieu de
    /// dériver du calendrier, chaque passage allongerait l'axe un peu plus — le défaut exact pour
    /// lequel la pause par étape a été retirée le 18/09/2026.
    /// </summary>
    [Fact]
    public void Relaying_twice_gives_the_same_axis()
    {
        var axis = Axis(5);
        var calendar = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));

        var once = PlanOf(axis, calendar);
        var applied = once.Columns
            .Select(c => new AxisColumn(c.Number, c.ToStart, c.ToEnd, IsMovedByHand: false))
            .ToList();

        var twice = PlanOf(applied, calendar);

        twice.ColumnsMoved.Should().Be(0, "the axis is derived from the calendar, never added to");
        twice.WorkingDaysRecovered.Should().Be(0);
        twice.AxisEndsOn.Should().Be(once.AxisEndsOn);
    }

    /// <summary>
    /// ⚠ <b>Révoquer rend les dates d'origine toutes seules.</b> Aucune table d'historique, rien à
    /// défaire : c'est ce qui autorise à déclarer, corriger et révoquer des fenêtres sans que
    /// personne ait à tenir le compte de ce que chacune avait poussé.
    /// </summary>
    [Fact]
    public void Revoking_the_window_and_relaying_restores_the_original_dates()
    {
        var weekends = Weekends();
        var original = Axis(5, weekends);

        var withWindow = WithWindow(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16));
        var pushed = PlanOf(original, withWindow).Columns
            .Select(c => new AxisColumn(c.Number, c.ToStart, c.ToEnd, IsMovedByHand: false))
            .ToList();

        // La fenêtre est révoquée : le calendrier redevient celui qui avait posé l'axe.
        var back = PlanOf(pushed, weekends);

        back.Columns.Select(c => (c.ToStart, c.ToEnd))
            .Should().Equal(original.Select(c => (c.StartDate, c.EndDate)));
    }

    // ─── Refus ──────────────────────────────────────────────────────────────────

    [Fact]
    public void An_axis_with_no_column_after_the_starting_one_is_refused()
    {
        var result = AxisRelayPlanner.Plan(Axis(3), Weekends(), ColumnLength, fromColumn: 9);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.NoColumnsToRelay");
    }

    [Fact]
    public void An_empty_axis_is_refused()
    {
        var result = AxisRelayPlanner.Plan([], Weekends(), ColumnLength, fromColumn: 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RotationCycle.NoColumnsToRelay");
    }

    /// <summary>
    /// ⚠ Une colonne déplacée à la main n'est pas « courte » : elle est là où quelqu'un l'a voulue,
    /// quelle que soit sa longueur. La proposer comme point de départ d'un rattrapage ferait
    /// commencer l'acte par écraser la seule chose qu'il doit épargner.
    /// </summary>
    [Fact]
    public void A_hand_moved_column_is_never_reported_as_the_first_short_one()
    {
        var axis = Axis(4);
        axis[0] = axis[0] with { EndDate = axis[0].EndDate.AddDays(-7), IsMovedByHand = true };

        var found = AxisRelayPlanner.FirstShortColumn(axis, Weekends(), ColumnLength);

        found.Should().NotBe(1);
    }

    [Fact]
    public void The_first_shortened_column_is_the_one_reported()
    {
        var axis = Axis(4);
        var calendar = WithWindow(new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 13));

        AxisRelayPlanner.FirstShortColumn(axis, calendar, ColumnLength).Should().Be(3);
    }
}
