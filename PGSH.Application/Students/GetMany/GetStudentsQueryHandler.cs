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

        if (request.Program.HasValue)
            query = query.Where(s => s.AcademicProgram == request.Program.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            // Trimmed and lowered on both sides: a pasted CNE carries stray spaces, and Appogee was
            // previously matched case-sensitively so "ap12" never found "AP12".
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.FirstName.ToLower().Contains(term) ||
                s.LastName.ToLower().Contains(term)  ||
                s.Email.ToLower().Contains(term)     ||
                s.CNE.ToLower().Contains(term)       ||
                s.Appogee.ToLower().Contains(term)   ||
                (s.CIN != null && s.CIN.ToLower().Contains(term)));
        }

        var response = await query
            .OrderBy(s => s.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new StudentSummaryResponse(
                    s.Id, s.Email, s.FirstName, s.LastName, s.CNE, s.Appogee, s.AcademicProgram.ToString(), s.CIN,
                    // The selected academic year's registration when a year is given (at most one),
                    // otherwise the most recent registration.
                    s.registrations
                        .Where(r => request.AcademicYearId == null || r.AcademicYearId == request.AcademicYearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.Level.Label)
                        .FirstOrDefault(),
                    s.registrations
                        .Where(r => request.AcademicYearId == null || r.AcademicYearId == request.AcademicYearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.AcademicGroup != null ? r.AcademicGroup.Label : null)
                        .FirstOrDefault(),
                    s.registrations
                        .Where(r => request.AcademicYearId == null || r.AcademicYearId == request.AcademicYearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.Status.ToString())
                        .FirstOrDefault()),
                ct);

        return Result.Success(response);
    }
}
