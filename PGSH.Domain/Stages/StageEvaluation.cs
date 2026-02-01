using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Domain.Stages;

//public sealed record StageEvaluation
//{
//    public string[]? Notes { get; init; }
//    public int? Score { get; init; } // nullable since not always evaluated
//    public StageAssignmentResult AssignmentResult { get; init; }

//    private StageEvaluation(string[]? notes, int? score, StageAssignmentResult result)
//    {
//        Notes = notes;
//        Score = score;
//        AssignmentResult = result;
//    }

//    public static StageEvaluation NotYetEvaluated() =>
//        new(null, null, StageAssignmentResult.NonÉvalué);

//    public static StageEvaluation Evaluated(string[]? notes, int score, StageAssignmentResult result)
//    {
//        if (score < 0)
//            throw new ArgumentException("Score cannot be negative.", nameof(score));

//        return new StageEvaluation(notes, score, result);
//    }
//}


public sealed class PeriodEvaluation : Entity
{
    public Guid Id { get; set; }

    public Guid InternshipAssignmentId { get; set; }
    public InternshipAssignment InternshipAssignment { get; set; }

    public Guid StageGroupPeriodId { get; set; }
    public StageGroupPeriod StageGroupPeriod { get; set; }

    public ICollection<ObjectiveEvaluation> ObjectiveEvaluations { get; set; }
        = new List<ObjectiveEvaluation>();

    public string? SupervisorComment { get; set; }
}

public sealed class ObjectiveEvaluation : Entity
{
    public Guid Id { get; set; }

    public int StageObjectiveId { get; set; }
    public StageObjective StageObjective { get; set; }

    public int Score { get; set; } // or enum/level
    public string? Note { get; set; }

    public Guid PeriodEvaluationId { get; set; }
    public PeriodEvaluation PeriodEvaluation { get; set; }
}

