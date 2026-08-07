using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// Membership is an append-only trail: a transfer closes the open record and opens a new one, so the
// group a student belonged to on any date stays reconstructable. A Temporary transfer is a loan — it
// remembers where to return and auto-reverts when the stage ends; a Definitive one never reverts.
public class CohortTransferTests
{
    private const int OriginCohort = 1;
    private const int TargetCohort = 2;

    private static readonly DateOnly Enrolled = new(2026, 1, 1);
    private static readonly DateOnly Moved    = new(2026, 2, 15);

    private static InternshipAssignment Enrolled_in_origin()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = OriginCohort };
        assignment.MembershipHistory.Add(new CohortMembership
        {
            InternshipAssignmentId = assignment.Id, CohortId = OriginCohort, StartDate = Enrolled,
        });
        return assignment;
    }

    private static ServicePeriod AddStartedPeriod(InternshipAssignment a)
    {
        var period = new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = a.Id, ServiceId = 10,
            StartDate = Enrolled, EndDate = new DateOnly(2026, 3, 31),
        };
        a.ServicePeriods.Add(period);
        a.Start().IsSuccess.Should().BeTrue();
        return period;
    }

    [Fact]
    public void A_transfer_closes_the_open_membership_and_opens_a_new_one()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, "Rapprochement familial", Moved);

        a.MembershipHistory.Should().HaveCount(2);
        var closed = a.MembershipHistory.Single(m => m.CohortId == OriginCohort);
        var open   = a.MembershipHistory.Single(m => m.CohortId == TargetCohort);
        closed.EndDate.Should().Be(Moved);
        open.StartDate.Should().Be(Moved);
        open.EndDate.Should().BeNull();
        a.CurrentCohortId.Should().Be(TargetCohort);
    }

    [Fact]
    public void A_definitive_transfer_records_no_return_address()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, "Changement de groupe", Moved, TransferType.Definitive);

        var open = a.MembershipHistory.Single(m => m.EndDate is null);
        open.TransferType.Should().Be(TransferType.Definitive);
        open.OriginalCohortId.Should().BeNull();
    }

    [Fact]
    public void A_temporary_transfer_remembers_where_to_return()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, "Prêt pour un stage", Moved, TransferType.Temporary);

        var open = a.MembershipHistory.Single(m => m.EndDate is null);
        open.TransferType.Should().Be(TransferType.Temporary);
        open.OriginalCohortId.Should().Be(OriginCohort);
    }

    [Fact]
    public void The_transfer_reason_is_kept_on_the_new_membership()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, "Motif médical", Moved);

        a.MembershipHistory.Single(m => m.EndDate is null).TransferReason.Should().Be("Motif médical");
    }

    [Fact]
    public void A_transfer_raises_its_domain_event_with_both_ends_and_the_type()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, "Prêt", Moved, TransferType.Temporary);

        var evt = a.DomainEvents.OfType<StudentCohortTransferredDomainEvent>().Should().ContainSingle().Subject;
        evt.PreviousCohortId.Should().Be(OriginCohort);
        evt.NewCohortId.Should().Be(TargetCohort);
        evt.Type.Should().Be(TransferType.Temporary);
    }

    [Fact]
    public void Keys_are_left_for_the_store_to_generate_on_the_new_membership()
    {
        var a = Enrolled_in_origin();

        a.TransferToCohort(TargetCohort, null, Moved);

        a.MembershipHistory.Single(m => m.EndDate is null).Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Closing_the_stage_ends_a_temporary_loan_and_sends_the_student_home()
    {
        var a = Enrolled_in_origin();
        var period = AddStartedPeriod(a);
        a.TransferToCohort(TargetCohort, "Prêt", Moved, TransferType.Temporary);

        a.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();

        a.Status.Should().Be(InternshipStatus.Completed);
        a.MembershipHistory.Single(m => m.CohortId == TargetCohort).EndDate
            .Should().NotBeNull("the loan ends when the stage it was made for ends");

        var evt = a.DomainEvents.OfType<TemporaryTransferEndedDomainEvent>().Should().ContainSingle().Subject;
        evt.OriginalCohortId.Should().Be(OriginCohort);
    }

    [Fact]
    public void Closing_the_stage_never_reverts_a_definitive_transfer()
    {
        var a = Enrolled_in_origin();
        var period = AddStartedPeriod(a);
        a.TransferToCohort(TargetCohort, "Changement définitif", Moved, TransferType.Definitive);

        a.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();

        a.MembershipHistory.Single(m => m.CohortId == TargetCohort).EndDate
            .Should().BeNull("a definitive move is permanent");
        a.DomainEvents.OfType<TemporaryTransferEndedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void A_stage_still_running_does_not_end_the_loan()
    {
        var a = Enrolled_in_origin();
        var first = AddStartedPeriod(a);
        a.ServicePeriods.Add(new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = a.Id, ServiceId = 11,
            StartDate = new DateOnly(2026, 4, 1), EndDate = new DateOnly(2026, 4, 30), IsStarted = true,
        });
        a.TransferToCohort(TargetCohort, "Prêt", Moved, TransferType.Temporary);

        a.CompletePeriod(first.Id).IsSuccess.Should().BeTrue();

        a.Status.Should().Be(InternshipStatus.Ongoing);
        a.MembershipHistory.Single(m => m.CohortId == TargetCohort).EndDate
            .Should().BeNull("one period closed is not the whole stage");
    }
}
