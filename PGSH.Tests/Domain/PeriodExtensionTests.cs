using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// <c>InternshipAssignment.ExtendTo</c> — repousser la fin d'une rotation sans toucher son début.
///
/// <para>L'acte existe parce que <c>Reschedule</c> ne pouvait rien pour le cas qui compte : une
/// fenêtre d'examens déclarée en cours d'année tombe sur des rotations <b>commencées</b>, que
/// <c>ServicePeriodLifecycle.Movable</c> refuse de déplacer — à juste titre, puisque quelque chose a
/// eu lieu à leur date de début. Ce qu'il reste à faire pour elles est de les allonger.</para>
///
/// <para>Ce que ces tests protègent, au-delà des refus : que l'acte soit <b>rejouable</b>. Un
/// recalcul d'axe est relancé chaque fois qu'une fenêtre est déclarée, corrigée ou révoquée, et un
/// acte qui ajouterait à ce qui est stocké allongerait la rotation à chaque passage — le défaut pour
/// lequel la pause par étape a été retirée le 18/09/2026 plutôt que réparée.</para>
/// </summary>
public class PeriodExtensionTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);
    private static readonly DateOnly End = new(2026, 3, 6);
    private static readonly DateOnly Later = new(2026, 3, 20);

    private static InternshipAssignment WithPeriod(out ServicePeriod period, bool started = true)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        period = new ServicePeriod
        {
            Id                     = Guid.NewGuid(),
            InternshipAssignmentId = assignment.Id,
            ServiceId              = 10,
            StartDate              = Start,
            EndDate                = End,
            IsStarted              = started,
        };
        assignment.ServicePeriods.Add(period);
        return assignment;
    }

    private static ServicePeriodRescheduledDomainEvent? EventOf(InternshipAssignment assignment) =>
        assignment.DomainEvents.OfType<ServicePeriodRescheduledDomainEvent>().SingleOrDefault();

    /// <summary>Le cas pour lequel l'acte existe : une rotation en cours s'allonge.</summary>
    [Fact]
    public void A_started_rotation_can_have_its_end_pushed()
    {
        var assignment = WithPeriod(out var period);

        var result = assignment.ExtendTo(period.Id, Later);

        result.IsSuccess.Should().BeTrue();
        period.EndDate.Should().Be(Later);
        period.StartDate.Should().Be(Start, "an extension never touches the start");
    }

    /// <summary>
    /// ⚠ Le contrôle qui donne son sens au précédent : le <i>déplacement</i> de la même rotation est
    /// toujours refusé. Sans lui, ce fichier prouverait seulement qu'une garde a été desserrée.
    /// </summary>
    [Fact]
    public void The_same_started_rotation_still_cannot_be_moved()
    {
        var assignment = WithPeriod(out var period);

        var result = assignment.Reschedule(period.Id, Start.AddDays(7), Later);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodCannotBeRescheduled);
        period.StartDate.Should().Be(Start);
        period.EndDate.Should().Be(End);
    }

    /// <summary>
    /// ⚠ <b>La propriété qui rend un recalcul d'axe relançable.</b> La même fin demandée deux fois ne
    /// doit rien faire — ni écrire, ni lever d'événement. Un acte qui ajouterait un delta aurait
    /// allongé deux fois, ce qui est exactement ce que <c>ResumePeriod</c> faisait.
    /// </summary>
    [Fact]
    public void Extending_twice_to_the_same_date_is_not_extending_twice()
    {
        var assignment = WithPeriod(out var period);

        assignment.ExtendTo(period.Id, Later);
        assignment.ClearDomainEvents();

        var again = assignment.ExtendTo(period.Id, Later);

        again.IsSuccess.Should().BeTrue();
        period.EndDate.Should().Be(Later);
        assignment.DomainEvents.Should().BeEmpty("nothing moved, so nothing happened");
    }

    /// <summary>
    /// ⚠ Un raccourcissement n'est pas un allongement, et le refus vit dans le nom de l'acte plutôt
    /// que dans une garde sur les présences : ramener la fin en arrière laisserait les journées
    /// pointées entre la nouvelle fin et l'ancienne sur des dates que la fenêtre ne couvre plus.
    /// </summary>
    [Fact]
    public void Pulling_the_end_earlier_is_refused()
    {
        var assignment = WithPeriod(out var period);

        var result = assignment.ExtendTo(period.Id, End.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.PeriodExtensionGoesBackwards");
        period.EndDate.Should().Be(End);
    }

    [Fact]
    public void A_closed_rotation_is_refused()
    {
        var assignment = WithPeriod(out var period);
        period.IsComplete = true;

        var result = assignment.ExtendTo(period.Id, Later);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodCannotBeExtended);
        period.EndDate.Should().Be(End);
    }

    [Fact]
    public void A_marked_rotation_is_refused()
    {
        var assignment = WithPeriod(out var period);
        period.Evaluation = new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 14m };

        var result = assignment.ExtendTo(period.Id, Later);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodCannotBeExtended);
        period.EndDate.Should().Be(End);
    }

    /// <summary>
    /// ⚠ Une rotation coupée par un transfert : sa fin <b>est</b> la date du transfert, un fait.
    /// L'allonger dirait que l'étudiant est resté après être parti.
    /// </summary>
    [Fact]
    public void An_interrupted_rotation_is_refused()
    {
        var assignment = WithPeriod(out var period);
        period.IsInterrupted = true;

        var result = assignment.ExtendTo(period.Id, Later);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodCannotBeExtended);
        period.EndDate.Should().Be(End);
    }

    [Fact]
    public void An_unknown_period_is_refused()
    {
        var assignment = WithPeriod(out _);

        var result = assignment.ExtendTo(Guid.NewGuid(), Later);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AssignmentPeriods.NotFound");
    }

    /// <summary>
    /// ⚠ <b>L'événement transporte les deux fenêtres, et son début est inchangé des deux côtés.</b>
    /// Sans l'ancienne fenêtre personne ne peut dire de combien la rotation a bougé ; et c'est le
    /// <i>même</i> événement que le déplacement, pour qu'un consommateur n'ait pas à s'abonner deux
    /// fois au même fait.
    /// </summary>
    [Fact]
    public void The_event_carries_both_windows_and_says_the_start_did_not_move()
    {
        var assignment = WithPeriod(out var period);

        assignment.ExtendTo(period.Id, Later);

        var raised = EventOf(assignment);
        raised.Should().NotBeNull();
        raised!.FromStartDate.Should().Be(Start);
        raised.ToStartDate.Should().Be(Start);
        raised.FromEndDate.Should().Be(End);
        raised.ToEndDate.Should().Be(Later);
    }

    /// <summary>
    /// Une rotation non démarrée s'allonge aussi. Les deux actes se recouvrent là, et c'est voulu :
    /// l'emboîtement des deux règles est ce qui permet au recalcul de n'en choisir qu'un par colonne
    /// sans avoir à traiter les cas limites.
    /// </summary>
    [Fact]
    public void A_planned_rotation_can_also_be_extended()
    {
        var assignment = WithPeriod(out var period, started: false);

        var result = assignment.ExtendTo(period.Id, Later);

        result.IsSuccess.Should().BeTrue();
        period.EndDate.Should().Be(Later);
    }
}
