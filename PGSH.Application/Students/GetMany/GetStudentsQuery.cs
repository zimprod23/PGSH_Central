using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetMany;

public record GetStudentsQuery(
    string? SearchTerm,
    string? CNE,
    string? Appogee,
    string? CIN,
    int PageNumber = 1,
    int PageSize = 10): IQuery<PaginatedResponse<StudentSummaryResponse>>;
