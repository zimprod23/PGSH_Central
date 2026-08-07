using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetMany;

public sealed record GetInternshipAssignmentsQuery(
    List<int>? CohortIds,
    int? StageId,
    Guid? RegistrationId,
    InternshipStatus? Status,
    // Notes-list scoping: partition (AcademicGroup.RotationGroup) + a single period (StageSlot period
    // number) the students must serve, plus a free-text match on name / appogée / CNE.
    List<string>? PartitionLabels = null,
    int? PeriodNumber = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResponse<InternshipAssignmentSummaryResponse>>;
