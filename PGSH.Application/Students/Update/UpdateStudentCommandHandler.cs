using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Update;

internal sealed class UpdateStudentCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateStudentCommand>
{
    public async Task<Result> Handle(UpdateStudentCommand request, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (student is null)
            return Result.Failure(StudentErrors.NotFound(request.Id));

        // ⚠ An absent CNE collides with nothing — `null == null` is true in memory and false in
        // SQL, so the comparison is guarded on the request value rather than left to the provider to
        // disagree about. IX_Student_CNE is filtered on IS NOT NULL and says the same thing.
        string? cne = StudentIdentifierRules.NormalizeCne(request.CNE);

        bool conflict = await dbContext.Students
            .AnyAsync(s => s.Id != request.Id && (
                s.Email == request.Email ||
                (cne != null && s.CNE == cne) ||
                s.Appogee == request.Appogee ||
                (request.CIN != null && s.CIN == request.CIN)),
                cancellationToken);

        if (conflict)
            return Result.Failure(StudentErrors.Conflict("Email/CNE/Appogee/CIN", request.Email));

        student.Email = request.Email;
        student.FirstName = request.FirstName;
        student.LastName = request.LastName;
        student.CIN = request.CIN;
        student.CNE = cne;
        student.Appogee = request.Appogee;
        student.AccessGrade = request.AccessGrade;
        student.AcademicProgram = request.AcademicProgram;
        student.BacSeries = request.BacSeries;
        student.BacYear = request.BacYear;
        student.Gender = request.Gender;
        student.Status = new Status(request.CivilStatus, request.NationalityStatus);
        student.PlaceOfBirth = request.PlaceOfBirth;
        student.Address = request.FullAddress;
        student.DateOfBirth = request.DateOfBirth;
        student.Academy = request.Academy;
        student.Province = request.Province;
        student.Ranking = request.Ranking;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
