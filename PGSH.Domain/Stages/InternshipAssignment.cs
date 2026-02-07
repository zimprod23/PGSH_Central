using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed class InternshipAssignment : Entity
{
    public Guid Id { get; set; }
    //To change later
    public InternshipStatus Status { get; set; } = InternshipStatus.Planned;

    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; }

    public int CurrentCohortId { get; set; }
    public Cohort Cohort { get; set; }

    public ICollection<ServicePeriod> ServicePeriods { get; set; } = new List<ServicePeriod>();
    public ICollection<CohortMembership> MembershipHistory { get; set; } = new List<CohortMembership>();

    // Derived values (stored, not authoritative)
    public decimal? FinalScore { get; private set; }
    public StageAssignmentResult? Result { get; private set; } = StageAssignmentResult.NonÉvalué;
}


public enum StageAssignmentResult
{
    NonÉvalué,
    Validé,
    NonValidé
}

