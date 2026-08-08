using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetMany;

/// <summary>
/// Groups, paged. There are 1,003 across the imported years and 101 in the current one alone, and a
/// promotion of 1,000 students adds 100 more each year — so this cannot return everything.
/// Callers wanting one year's groups should always pass <paramref name="AcademicYearId"/>.
/// </summary>
public sealed record GetAcademicGroupsQuery(
    int?    AcademicYearId = null,
    int?    LevelId        = null,
    Guid?   StudentId      = null,
    int     PageNumber     = 1,
    int     PageSize       = 100,
    string? SearchTerm     = null)
    : IQuery<PaginatedResponse<AcademicGroupResponse>>;

public sealed record AcademicGroupResponse(
    int     Id,
    string  Label,
    int     GroupNumber,
    int     AcademicYearId,
    string  AcademicYearLabel,
    string? RotationGroup,
    int?    LevelId,
    string? LevelLabel,
    /// <summary>Roster size, so the list can show it without loading any student.</summary>
    int     StudentCount);
