using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Assign;

internal sealed class AssignStudentsToCohortCommandHandler(StudentAffectationService affectation)
    : ICommandHandler<AssignStudentsToCohortCommand, BulkResponse<Guid, Guid>>
{
    public Task<Result<BulkResponse<Guid, Guid>>> Handle(
        AssignStudentsToCohortCommand request, CancellationToken cancellationToken)
        => affectation.AssignToCohortAsync(request.CohortId, cancellationToken);
}
