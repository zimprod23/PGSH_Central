using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Timeline;

public sealed record GetYearTimelineQuery(int AcademicYearId, int? LevelId) : IQuery<YearTimelineResponse>;

public sealed record YearTimelineResponse(
    int       AcademicYearId,
    string    AcademicYearLabel,
    DateOnly? Start,
    DateOnly? End,
    IReadOnlyList<TimelineLevel> Levels);

public sealed record TimelineLevel(
    int     LevelId,
    string? LevelLabel,
    DateOnly? Start,
    DateOnly? End,
    IReadOnlyList<TimelineStage> Stages);

public sealed record TimelineStage(
    int       StageId,
    string    StageName,
    DateOnly? Start,
    DateOnly? End,
    int       SlotCount,
    int       CohortCount,
    int       PartitionCount,
    bool      HasSaturation,
    IReadOnlyList<TimelinePartition> Partitions);

public sealed record TimelinePartition(
    string?   Label,
    DateOnly? Start,
    DateOnly? End,
    int       CohortCount,
    int       StudentCount,
    bool      Saturated,
    IReadOnlyList<TimelineGroup> Groups);

public sealed record TimelineGroup(
    int    GroupId,
    string GroupLabel,
    int    GroupNumber,
    int    StudentCount);
