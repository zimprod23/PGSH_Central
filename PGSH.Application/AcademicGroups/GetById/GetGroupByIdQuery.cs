using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.GetById;

public sealed record GetGroupByIdQuery(int Id) : IQuery<GroupDetailResponse>;

public sealed record GroupDetailResponse(
    int    Id,
    string Label,
    int    GroupNumber,
    string? GeographicZone,
    string? RotationGroup,
    int    AcademicYearId,
    string AcademicYearLabel,
    IReadOnlyList<GroupStudentResponse> Students);

public sealed record GroupStudentResponse(
    Guid   RegistrationId,
    Guid   StudentId,
    string FullName,
    string Cne,
    string Email,
    string RegistrationStatus);
