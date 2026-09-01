namespace PGSH.Application.Stages.Cohorts.GetById;

public sealed record CohortResponse(
    int     Id,
    int     StageId,
    string  StageName,
    int     AcademicGroupId,
    string  AcademicGroupLabel,
    string  Label,
    int     StudentAssignmentCount,
    int     SlotAssignmentCount,
    bool    IsSchedulePublished,
    int     AcademicYearId,
    string  AcademicYearLabel,
    string? RotationGroup,
    /// <summary>
    /// The columns of the axis this cohorte actually stands in.
    /// </summary>
    /// <remarks>
    /// ⚠ Here rather than read off the planning grid. « N'agir que sur P4-P6 » has to know which
    /// cohortes run in those columns, and that used to be folded out of the grid response — which
    /// was only ever correct while the grid shipped every cohorte and every cell. It is a fact about
    /// the cohorte, so it belongs on the cohorte.
    /// </remarks>
    IReadOnlyList<int> PeriodNumbers);

public sealed record CohortDetailResponse(
    int    Id,
    int    StageId,
    string StageName,
    int    AcademicGroupId,
    string AcademicGroupLabel,
    string Label,
    int    StudentAssignmentCount,
    bool   IsSchedulePublished,
    IReadOnlyList<CohortSlotDetail> SlotAssignments);

public sealed record CohortSlotDetail(
    int      AssignmentId,
    int      StageSlotId,
    int      PeriodNumber,
    string?  PeriodLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    int      ServiceId,
    string   ServiceName,
    string   HospitalName);
