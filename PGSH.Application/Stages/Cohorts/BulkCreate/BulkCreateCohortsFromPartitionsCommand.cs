using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.BulkCreate;

public sealed record BulkCreateCohortsFromPartitionsCommand(
    int AcademicYearId,
    IReadOnlyList<PartitionStagePair> Mappings
) : ICommand<BulkCohortsFromPartitionsResult>;

public sealed record PartitionStagePair(string RotationGroup, int StageId);

/// <summary><paramref name="NotRequiredByCnpn"/> counts group×stage pairs refused because the
/// group's CNPN does not require that stage of its level — usually a mis-ticked row.</summary>
public sealed record BulkCohortsFromPartitionsResult(int Created, int Skipped, int NotRequiredByCnpn);
