using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetById;

/// <summary>
/// One group with a <em>page</em> of its students. The roster is paginated because a group is not
/// necessarily small: the legacy import lands every registration without a group number into a
/// per-year "Non réparti" bucket, and for 2025-2026 that single group holds 4,725 students. Returning
/// them in one response — each with two correlated loan lookups — is what took the browser down.
/// </summary>
public sealed record GetGroupByIdQuery(
    int     Id,
    int     PageNumber  = 1,
    int     PageSize    = 25,
    string? SearchTerm  = null) : IQuery<GroupDetailResponse>;

public sealed record GroupDetailResponse(
    int    Id,
    string Label,
    int    GroupNumber,
    string? GeographicZone,
    string? RotationGroup,
    int    AcademicYearId,
    string AcademicYearLabel,
    /// <summary>Total students in the group, independent of the page being viewed.</summary>
    int    StudentCount,
    PaginatedResponse<GroupStudentResponse> Students,
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
