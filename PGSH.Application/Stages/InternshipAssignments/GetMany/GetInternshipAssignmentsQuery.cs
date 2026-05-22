using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetMany;

public sealed record GetInternshipAssignmentsQuery(
    int? CohortId,
    int? StageId,
    Guid? RegistrationId,
    InternshipStatus? Status,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResponse<InternshipAssignmentSummaryResponse>>;
