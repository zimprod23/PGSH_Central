using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// <c>StageSlot.Source</c> — qui a décidé des dates d'une colonne, et ce que cela met hors de portée
/// d'un recalcul d'axe.
///
/// <para>Le pendant de <c>CellSource</c> d'un cran plus haut. <c>CohortSlotAssignment.Source</c>
/// empêche l'arrangeur de réécrire une <i>cellule</i> qu'un humain a choisie ; rien n'empêchait un
/// recalcul d'axe de réécrire les <i>dates</i> qu'un humain a choisies — la même faute, atteinte par
/// l'autre bout.</para>
///
/// <para>⚠ Ce que ces tests protègent avant tout est que le marquage ne soit <b>pas séparable</b> du
/// déplacement. Deux instructions — écrire les dates, puis poser le drapeau — c'est une occasion
/// d'en écrire une sans l'autre, et la moitié qui manque est silencieuse : la colonne a l'air
/// normale jusqu'au recalcul qui l'écrase.</para>
/// </summary>
public class SlotSourceTests
{
    private const int StageId = 7, YearId = 22, PeriodNumber = 3;

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End = new(2026, 3, 13);
    private static readonly DateOnly NewStart = new(2026, 3, 9);
    private static readonly DateOnly NewEnd = new(2026, 3, 20);

    private static StageSlot Slot() =>
        StageSlot.For(StageId, YearId, PeriodNumber, Start, End, "P3").Value;

    /// <summary>
    /// ⚠ La valeur par défaut décide du sens de **toutes** les lignes déjà en base : la colonne est
    /// ajoutée par une migration qui ne reclasse rien, donc un créneau posé avant elle doit se lire
    /// « posé par l'axe ».
    /// </summary>
    [Fact]
    public void A_column_the_axis_laid_is_Laid()
    {
        var slot = Slot();

        slot.Source.Should().Be(SlotSource.Laid);
        slot.IsMovedByHand.Should().BeFalse();
    }

    /// <summary>Le fait central : déplacer et marquer sont un seul geste.</summary>
    [Fact]
    public void Moving_a_column_by_hand_marks_it_in_the_same_gesture()
    {
        var slot = Slot();

        var result = slot.MoveTo(NewStart, NewEnd);

        result.IsSuccess.Should().BeTrue();
        slot.StartDate.Should().Be(NewStart);
        slot.EndDate.Should().Be(NewEnd);
        slot.IsMovedByHand.Should().BeTrue("the dates and the mark are one fact");
    }

    /// <summary>
    /// ⚠ Le refus qui donne sa raison d'être au marqueur. Sans lui, reposer l'axe efface la décision
    /// d'un humain — prise, le plus souvent, pour une raison que la grille ne connaît pas.
    /// </summary>
    [Fact]
    public void The_axis_refuses_to_relay_a_column_moved_by_hand()
    {
        var slot = Slot();
        slot.MoveTo(NewStart, NewEnd);

        var result = slot.RelayTo(Start, End);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotMovedByHandCannotBeRelaid");
        slot.StartDate.Should().Be(NewStart, "a refused relay writes nothing");
        slot.EndDate.Should().Be(NewEnd);
    }

    /// <summary>
    /// ⚠ Le contrôle : sans lui les assertions ci-dessus seraient satisfaites par un
    /// <c>RelayTo</c> qui refuse tout.
    /// </summary>
    [Fact]
    public void The_axis_relays_a_column_it_laid_itself()
    {
        var slot = Slot();

        var result = slot.RelayTo(NewStart, NewEnd);

        result.IsSuccess.Should().BeTrue();
        slot.StartDate.Should().Be(NewStart);
        slot.EndDate.Should().Be(NewEnd);
        slot.Source.Should().Be(SlotSource.Laid, "the machine writing is the default, not a decision");
    }

    /// <summary>
    /// Reposer deux fois de suite reste possible — un recalcul est relancé après chaque fenêtre
    /// déclarée, corrigée ou révoquée, et une colonne qu'il a lui-même posée n'est pas devenue la
    /// décision de quelqu'un entre-temps.
    /// </summary>
    [Fact]
    public void Relaying_is_replayable()
    {
        var slot = Slot();

        slot.RelayTo(NewStart, NewEnd).IsSuccess.Should().BeTrue();
        slot.RelayTo(Start, End).IsSuccess.Should().BeTrue();

        slot.StartDate.Should().Be(Start);
        slot.EndDate.Should().Be(End);
    }

    [Fact]
    public void A_reversed_window_is_refused_by_both_doors()
    {
        var byHand = Slot();
        var byAxis = Slot();

        byHand.MoveTo(End, Start).IsFailure.Should().BeTrue();
        byAxis.RelayTo(End, Start).IsFailure.Should().BeTrue();

        byHand.StartDate.Should().Be(Start, "a refused move writes nothing");
        byHand.IsMovedByHand.Should().BeFalse("and marks nothing either");
        byAxis.StartDate.Should().Be(Start);
    }

    /// <summary>
    /// ⚠ Un refus de relais est un filet, pas un cas d'usage : le planificateur écarte ces colonnes
    /// et les compte avant d'arriver ici. La garde vit tout de même dans l'objet, parce qu'un
    /// invariant qui repose sur la bonne volonté de son appelant n'en est pas un — la leçon de
    /// <c>InternshipAssignment.Reschedule</c>, dont la règle vivait entièrement dans
    /// <c>PublishedPeriodShifter.PlanAsync</c>.
    /// </summary>
    [Fact]
    public void A_hand_moved_column_can_still_be_moved_again_by_hand()
    {
        var slot = Slot();
        slot.MoveTo(NewStart, NewEnd);

        var again = slot.MoveTo(Start, End);

        again.IsSuccess.Should().BeTrue("the human who moved it may correct himself");
        slot.IsMovedByHand.Should().BeTrue();
    }
}
