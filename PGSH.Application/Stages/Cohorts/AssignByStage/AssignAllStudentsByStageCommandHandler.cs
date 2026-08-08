using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.AssignByStage;

internal sealed class AssignAllStudentsByStageCommandHandler(
    AcademicYearResolver yearResolver,
    StudentAffectationService affectation)
    : ICommandHandler<AssignAllStudentsByStageCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        AssignAllStudentsByStageCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<BulkResponse<Guid, Guid>>(year.Error);

        var response = await affectation.AssignByStageAsync(
            request.StageId, year.Value, request.PartitionLabels, cancellationToken);
        return Result.Success(response);
    }
}
