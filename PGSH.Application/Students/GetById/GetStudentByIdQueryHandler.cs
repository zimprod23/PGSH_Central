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
                s.Ranking
                // Map the collections efficiently
                //s.registrations.Select(r => new RegistrationResponse(
                //    r.Id, r.AcademicYear, r.Status, r.Level.ToString())).ToList(),
                //s.history.Select(h => new HistoryResponse(
                //    h.Action, h.Date, h.Description)
                
                //).ToList()
            ))
            .FirstOrDefaultAsync(ct);

        return student is null
            ? Result.Failure<StudentResponse>(StudentErrors.NotFound(request.StudentId))
            : student;
    }
}