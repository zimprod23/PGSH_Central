using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public sealed class InternshipAssignment : Entity
{
    public Guid Id { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public decimal Score { get; set; } = 9.99M;
    public int StageGroupId { get; set; } = 0;
    public StageGroup StageGroup { get; set; }
    public ICollection<AssignmentPeriod> Periods { get; set; } = new List<AssignmentPeriod>();
    //public StageEvaluation evaluation { get; set; } = StageEvaluation.NotYetEvaluated();
    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; }

}

public enum StageAssignmentResult
{
    NonÉvalué,
    Validé,
    NonValidé
}

public sealed record StageEvaluation
{
    public string[]? Notes { get; init; }
    public int? Score { get; init; } // nullable since not always evaluated
    public StageAssignmentResult AssignmentResult { get; init; }

    private StageEvaluation(string[]? notes, int? score, StageAssignmentResult result)
    {
        Notes = notes;
        Score = score;
        AssignmentResult = result;
    }

    public static StageEvaluation NotYetEvaluated() =>
        new(null, null, StageAssignmentResult.NonÉvalué);

    public static StageEvaluation Evaluated(string[]? notes, int score, StageAssignmentResult result)
    {
        if (score < 0)
            throw new ArgumentException("Score cannot be negative.", nameof(score));

        return new StageEvaluation(notes, score, result);
    }
}
