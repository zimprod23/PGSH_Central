using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Assign;

public sealed record AssignStudentsToCohortCommand(int CohortId) : ICommand<BulkResponse<Guid, Guid>>;
