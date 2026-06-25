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
    IReadOnlyList<GroupStudentResponse> Students,
    IReadOnlyList<IncomingLoanResponse> IncomingLoans);

public sealed record GroupStudentResponse(
    Guid   RegistrationId,
    Guid   StudentId,
    string FullName,
    string Cne,
    string Email,
    string RegistrationStatus,
    // Set when the student is on a temporary loan to another group for one stage; they stay
    // registered here and auto-revert at that stage's end.
    string? LoanedToGroup,
    string? LoanedStage);

public sealed record IncomingLoanResponse(
    Guid   StudentId,
    string FullName,
    string Cne,
    string FromGroup,
    string Stage);
