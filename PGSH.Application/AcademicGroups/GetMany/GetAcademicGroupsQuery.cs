using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.GetMany;

public sealed record GetAcademicGroupsQuery(int? AcademicYearId = null, int? LevelId = null)
    : IQuery<List<AcademicGroupResponse>>;

public sealed record AcademicGroupResponse(
    int     Id,
    string  Label,
    int     GroupNumber,
    int     AcademicYearId,
    string  AcademicYearLabel,
    string? RotationGroup);
