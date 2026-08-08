using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Timeline;

/// <summary>
/// The Year → Level → Stage → Partition tree behind the calendar.
/// </summary>
/// <param name="StageId">
/// Narrows the tree to one stage. The overview legitimately needs the whole year, but drilling into a
/// stage's détail/répartitions does not: a year holds 1,684 cohorts on the imported data, and building
/// every one of them — plus each cohort's service periods and pause bands — to render a single stage
/// is what makes the detail view crawl.
/// </param>
public sealed record GetYearTimelineQuery(int AcademicYearId, int? LevelId, int? StageId = null)
    : IQuery<YearTimelineResponse>;

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
    IReadOnlyList<TimelineGroup> Groups,
    IReadOnlyList<TimelinePauseBand> Pauses);

// A suspension window over a partition's rotation (e.g. an exam week), drawn as a hatched band on
// the calendar. End is null while still paused (open-ended). Kind is the pause reason category.
public sealed record TimelinePauseBand(
    DateOnly  Start,
    DateOnly? End,
    string    Kind);

public sealed record TimelineGroup(
    int    GroupId,
    string GroupLabel,
    int    GroupNumber,
    int    StudentCount);
