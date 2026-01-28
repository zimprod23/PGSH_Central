using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetMany;

internal sealed class GetStudentsQueryHandler(IApplicationDbContext context): IQueryHandler<GetStudentsQuery, PaginatedResponse<StudentSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StudentSummaryResponse>>> Handle(
        GetStudentsQuery request,
        CancellationToken ct)
    {
        // 1. Setup Query with AsNoTracking (Best performance for reads)
        IQueryable<Student> query = context.Students.AsNoTracking();

        // 2. Apply Filters (Only if they have values)
        if (!string.IsNullOrWhiteSpace(request.CNE))
            query = query.Where(s => s.CNE == request.CNE);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.ToLower();
            query = query.Where(s =>
                s.FirstName.ToLower().Contains(term) ||
                s.LastName.ToLower().Contains(term) ||
                s.Email.ToLower().Contains(term));
        }

        // 3. Count Total (Required for UI pagination metadata)
        // We do this BEFORE Skip/Take
        int totalCount = await query.CountAsync(ct);

        // 4. Projection & Pagination
        // Performance: We only 'Select' the summary fields to keep the SQL row narrow
        var items = await query
            .OrderBy(s => s.LastName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new StudentSummaryResponse(
                s.Id,
                s.Email,
                s.FirstName,
                s.LastName,
                s.CNE,
                s.Appogee,
                s.AcademicProgram.ToString(),
                s.CIN))
            .ToListAsync(ct);

        // 5. Wrap in Result and PagedResponse
        var response = new PaginatedResponse<StudentSummaryResponse>(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
