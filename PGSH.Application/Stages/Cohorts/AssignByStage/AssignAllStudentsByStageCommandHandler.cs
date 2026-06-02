using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.AssignByStage;

internal sealed class AssignAllStudentsByStageCommandHandler(StudentAffectationService affectation)
    : ICommandHandler<AssignAllStudentsByStageCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        AssignAllStudentsByStageCommand request, CancellationToken cancellationToken)
    {
        var response = await affectation.AssignByStageAsync(request.StageId, request.PartitionLabels, cancellationToken);
        return Result.Success(response);
    }
}
