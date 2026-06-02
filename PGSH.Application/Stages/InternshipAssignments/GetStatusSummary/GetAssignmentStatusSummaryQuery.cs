using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.InternshipAssignments.GetStatusSummary;

public sealed record GetAssignmentStatusSummaryQuery(
    List<int>? CohortIds,
    int?       StageId) : IQuery<List<AssignmentStatusCount>>;

public sealed record AssignmentStatusCount(InternshipStatus Status, int Count);
