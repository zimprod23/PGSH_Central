using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.BulkCreate;

internal sealed class BulkCreateCohortsFromPartitionsCommandHandler(CohortProvisioner provisioner)
    : ICommandHandler<BulkCreateCohortsFromPartitionsCommand, BulkCohortsFromPartitionsResult>
{
    public async Task<Result<BulkCohortsFromPartitionsResult>> Handle(
        BulkCreateCohortsFromPartitionsCommand request, CancellationToken cancellationToken)
    {
        var mappings = request.Mappings.Select(m => (m.RotationGroup, m.StageId)).ToList();

        var result = await provisioner.EnsureCohortsAsync(request.AcademicYearId, mappings, cancellationToken);
        if (result.IsFailure)
            return Result.Failure<BulkCohortsFromPartitionsResult>(result.Error);

        if (result.Value.MatchedGroups == 0)
            return Result.Failure<BulkCohortsFromPartitionsResult>(
                Error.Validation("MacroPlan.NoPartitionedGroups",
                    "No groups with rotation labels found. Run stage auto-arrange first to assign partition labels to groups."));

        return Result.Success(new BulkCohortsFromPartitionsResult(
            result.Value.Created, result.Value.Skipped, result.Value.NotRequiredByCnpn));
    }
}
