using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.BulkCreate;

public sealed record BulkCreateCohortsFromPartitionsCommand(
    int AcademicYearId,
    IReadOnlyList<PartitionStagePair> Mappings
) : ICommand<BulkCohortsFromPartitionsResult>;

public sealed record PartitionStagePair(string RotationGroup, int StageId);

public sealed record BulkCohortsFromPartitionsResult(int Created, int Skipped);
