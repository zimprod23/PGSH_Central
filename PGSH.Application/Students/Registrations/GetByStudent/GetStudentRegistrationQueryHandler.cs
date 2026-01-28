using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.GetByStudent;

internal sealed class GetStudentRegistrationsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetStudentRegistrationsQuery, List<StudentRegistrationResponse>>
{
    public async Task<Result<List<StudentRegistrationResponse>>> Handle(
        GetStudentRegistrationsQuery request,
        CancellationToken ct)
    {
        // 1. Verify student exists first (optional but safer)
        var studentExists = await dbContext.Students
            .AnyAsync(s => s.Id == request.StudentId, ct);

        if (!studentExists)
            return Result.Failure<List<StudentRegistrationResponse>>(StudentErrors.NotFound(request.StudentId));

        // 2. Project registrations into the response
        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == request.StudentId)
            .OrderByDescending(r => r.AcademicYear) // Most recent first
            .Select(r => new StudentRegistrationResponse(
                r.Id,
                r.AcademicYear,
                r.LevelId,
                r.Status,
                r.failureReasons != null,
                r.failureReasons != null ? r.failureReasons.Description : null
            ))
            .ToListAsync(ct);

        return registrations;
    }
}
