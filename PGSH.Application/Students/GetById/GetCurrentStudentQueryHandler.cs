using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetById;

public sealed record GetCurrentStudentQuery(): IQuery<StudentResponse>;
internal sealed class GetCurrentStudentQueryHandler(IApplicationDbContext dbContext, IUserContext userContext) : IQueryHandler<GetCurrentStudentQuery, StudentResponse>
{
    public async Task<Result<StudentResponse>> Handle(GetCurrentStudentQuery request, CancellationToken cancellationToken)
    {
        //Fetching the user Id
        var userId = userContext.UserId;
        var student = await dbContext.Students
           .AsNoTracking()
           .Include(s => s.registrations) // Load the collection
           .Include(s => s.history)       // Load the collection
           .Where(s => s.IdentityProviderId == userId.ToString())
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
           // Map the collections efficiently
           //s.registrations.Select(r => new RegistrationResponse(
           //    r.Id, r.AcademicYear, r.Status, r.Level.ToString())).ToList(),
           //s.history.Select(h => new HistoryResponse(
           //    h.Action, h.Date, h.Description)

           //).ToList()
           ))
           .FirstOrDefaultAsync(cancellationToken);

        return student is null
            ? Result.Failure<StudentResponse>(StudentErrors.NotFound(userId))
            : student;
    }
}
