using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetMany;

internal sealed class GetStudentsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetStudentsQuery, PaginatedResponse<StudentSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StudentSummaryResponse>>> Handle(
        GetStudentsQuery request, CancellationToken ct)
    {
        IQueryable<Student> query = context.Students.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.CNE))
            query = query.Where(s => s.CNE == request.CNE);

        if (!string.IsNullOrWhiteSpace(request.Appogee))
            query = query.Where(s => s.Appogee == request.Appogee);

        if (!string.IsNullOrWhiteSpace(request.CIN))
            query = query.Where(s => s.CIN == request.CIN);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(s =>
                s.FirstName.ToLower().Contains(term) ||
                s.LastName.ToLower().Contains(term) ||
                s.Email.ToLower().Contains(term)     ||
                s.CNE.ToLower().Contains(term)       ||
                s.Appogee.Contains(term)             ||
                (s.CIN != null && s.CIN.ToLower().Contains(term)));
        }

        var response = await query
            .OrderBy(s => s.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new StudentSummaryResponse(s.Id, s.Email, s.FirstName, s.LastName, s.CNE, s.Appogee, s.AcademicProgram.ToString(), s.CIN),
                ct);

        return Result.Success(response);
    }
}
