using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

internal sealed class GetStudentByIdQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetStudentByIdQuery, StudentResponse>
{
    public async Task<Result<StudentResponse>> Handle(GetStudentByIdQuery request, CancellationToken ct)
    {
        var student = await context.Students
            .AsNoTracking()
            .Include(s => s.registrations) // Load the collection
            .Include(s => s.history)       // Load the collection
            .Where(s => s.Id == request.StudentId)
            .Select(s => new StudentResponse(
                s.Id,
                s.Email,
                s.FirstName,
                s.LastName,
                s.CIN,
                s.Gender.ToString(),
                s.Status.CivilStatus.ToString(),
                s.Status.NationalityStatus.ToString(),
                s.DateOfBirth,
                s.PlaceOfBirth,
                s.Address != null ? s.Address.FullAddress : null,
                s.CNE,
                s.Appogee,
                s.AcademicProgram.ToString(),
                s.BacSeries.ToString(),
                s.BacYear,
                s.AccessGrade,
                s.Ranking,
                s.registrations
                    .OrderByDescending(r => r.AcademicYear)
                    .Select(r => new StudentRegistrationSummary(
                        r.Id,
                        r.AcademicYear,
                        r.Status.ToString(),
                        new LevelResponse(
                            r.Level.Label,
                            r.Level.Year,
                            r.Level.AcademicProgram.ToString()
                        )
                    ))
                    .FirstOrDefault()
            ))
            .FirstOrDefaultAsync(ct);

        return student is null
            ? Result.Failure<StudentResponse>(StudentErrors.NotFound(request.StudentId))
            : student;
    }
}