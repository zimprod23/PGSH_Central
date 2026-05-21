using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.GetByStudent;

internal sealed class GetStudentRegistrationsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetStudentRegistrationsQuery, List<StudentRegistrationResponse>>
{
    public async Task<Result<List<StudentRegistrationResponse>>> Handle(
        GetStudentRegistrationsQuery request,
        CancellationToken ct)
    {
        var studentExists = await dbContext.Students
            .AnyAsync(s => s.Id == request.StudentId, ct);

        if (!studentExists)
            return Result.Failure<List<StudentRegistrationResponse>>(StudentErrors.NotFound(request.StudentId));

        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == request.StudentId)
            .OrderByDescending(r => r.AcademicYear.StartDate)
            .Select(r => new StudentRegistrationResponse(
                r.Id,
                r.AcademicYearId,
                r.AcademicYear.Label,
                r.LevelId,
                r.Level.Label,
                r.Status.ToString(),
                r.failureReasons != null,
                r.failureReasons != null ? r.failureReasons.Description : null
            ))
            .ToListAsync(ct);

        return registrations;
    }
}
