using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Domain;

// Délocalisation: the whole stage is served outside the faculty. The in-faculty rotation never
// happens, so the planned periods are dropped and replaced by one ad-hoc period at the external
// service — already started and closed, since the stage is over by the time it is recorded. It is
// whole-stage only: once any in-faculty period has begun, the move is refused.
public class DelocalizationTests
{
    private const int StageId    = 1;
    private const int ExternalId = 99;

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    private static InternshipAssignment WithPlannedPeriods(int count)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        for (int i = 0; i < count; i++)
            assignment.ServicePeriods.Add(new ServicePeriod
            {
                Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id, ServiceId = 10 + i,
                CohortSlotAssignmentId = i + 1,
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            });
        return assignment;
    }

    private static Result Delocalize(InternshipAssignment a, string reason = "Stage effectué à Casablanca") =>
        a.Delocalize(StageId, ExternalId, Start, End, reason, demandeId: null);

    [Fact]
    public void Planned_periods_are_replaced_by_a_single_external_period()
    {
        var a = WithPlannedPeriods(3);

        Delocalize(a).IsSuccess.Should().BeTrue();

        var period = a.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(ExternalId);
        period.CohortSlotAssignmentId.Should().BeNull("a délocalisation belongs to no schedule cell");
        period.StartDate.Should().Be(Start);
        period.EndDate.Should().Be(End);
    }

    [Fact]
    public void The_external_period_is_created_already_started_and_closed()
    {
        var a = WithPlannedPeriods(1);

        Delocalize(a).IsSuccess.Should().BeTrue();

        var period = a.ServicePeriods.Single();
        period.IsStarted.Should().BeTrue();
        period.IsComplete.Should().BeTrue();
        period.IsDelocalized.Should().BeTrue();
        a.Status.Should().Be(InternshipStatus.Completed);
    }

    [Fact]
    public void The_motif_is_carried_on_the_delocalization_record()
    {
        var a = WithPlannedPeriods(1);
        var demandeId = Guid.NewGuid();

        a.Delocalize(StageId, ExternalId, Start, End, "Raison familiale", demandeId).IsSuccess.Should().BeTrue();

        var delocalization = a.ServicePeriods.Single().Delocalization;
        delocalization.Should().NotBeNull();
        delocalization!.Reason.Should().Be("Raison familiale");
        delocalization.DemandeId.Should().Be(demandeId);
    }

    [Fact]
    public void Keys_are_left_for_the_store_to_generate()
    {
        var a = WithPlannedPeriods(1);

        Delocalize(a).IsSuccess.Should().BeTrue();

        // Pre-setting a store-generated key on a child of a tracked parent makes EF issue an UPDATE
        // against a non-existent row. The domain deliberately leaves these empty.
        var period = a.ServicePeriods.Single();
        period.Id.Should().Be(Guid.Empty);
        period.Delocalization!.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void A_delocalization_raises_its_domain_event()
    {
        var a = WithPlannedPeriods(1);

        Delocalize(a, "Stage à l'étranger").IsSuccess.Should().BeTrue();

        var evt = a.DomainEvents.OfType<StudentDelocalizedDomainEvent>().Should().ContainSingle().Subject;
        evt.StageId.Should().Be(StageId);
        evt.ServiceId.Should().Be(ExternalId);
        evt.Reason.Should().Be("Stage à l'étranger");
    }

    [Fact]
    public void A_stage_already_underway_cannot_be_delocalized()
    {
        var a = WithPlannedPeriods(2);
        a.Start().IsSuccess.Should().BeTrue();   // every period is now started

        var result = Delocalize(a);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.StageAlreadyUnderway);
        a.ServicePeriods.Should().HaveCount(2, "nothing may be dropped when the move is refused");
    }

    [Fact]
    public void A_stage_with_an_interrupted_period_cannot_be_delocalized()
    {
        var a = WithPlannedPeriods(1);
        a.ServicePeriods.Single().IsInterrupted = true;

        var result = Delocalize(a);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.StageAlreadyUnderway);
    }

    [Fact]
    public void The_external_period_is_immediately_evaluable_and_rolls_up_to_a_verdict()
    {
        var a = WithPlannedPeriods(1);
        Delocalize(a).IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();

        var result = a.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.ValidatePeriod,
            Outcome = EvaluationOutcome.Validated,
            FicheReference = "fiche-2026-0042",
        });

        result.IsSuccess.Should().BeTrue();
        a.FinalScore.Should().Be(10m, "a validate-only verdict maps onto the 0–20 scale");
        a.Result.Should().Be(StageAssignmentResult.Validé);
        a.Status.Should().Be(InternshipStatus.Evaluated);
    }

    [Fact]
    public void A_refused_external_stage_rolls_up_as_not_validated()
    {
        var a = WithPlannedPeriods(1);
        Delocalize(a).IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();

        a.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.ValidatePeriod,
            Outcome = EvaluationOutcome.NotValidated,
        }).IsSuccess.Should().BeTrue();

        a.FinalScore.Should().Be(0m);
        a.Result.Should().Be(StageAssignmentResult.NonValidé);
    }
}
