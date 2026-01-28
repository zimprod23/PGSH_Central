using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using PGSH.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace PGSH.Application.Students.Create
{
    internal sealed class CreateStudentCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CreateStudentCommand, Guid>
    {
        public async Task<Result<Guid>> Handle(CreateStudentCommand request, CancellationToken ct)
        {
            // 1. Uniqueness check
            // 1. Single DB trip to find any matching record
            var existing = await context.Students
                .Where(s => s.CNE == request.CNE ||
                            s.Email == request.Email ||
                            s.CIN == request.CIN ||
                            s.Appogee == request.Appogee)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // 2. Determine which field matched using a simple check
                var culprit = existing.CNE == request.CNE ? ("CNE", request.CNE) :
                              existing.Email == request.Email ? ("Email", request.Email) :
                              existing.Appogee == request.Appogee ? ("Appogee", request.Appogee) :
                              ("CIN", request.CIN!);

                return Result.Failure<Guid>(StudentErrors.Conflict(culprit.Item1, culprit.Item2));
            }

            // 2. Map Entity
            var student = new Student
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CIN = request.CIN,
                Gender = request.Gender,
                Status = new Status(request.CivilStatus, request.NationalityStatus),
                DateOfBirth = request.DateOfBirth,
                PlaceOfBirth = request.PlaceOfBirth,
                Address = request.FullAddress, // Uses your implicit operator

                // Student specific
                CNE = request.CNE,
                Appogee = request.Appogee,
                AccessGrade = request.AccessGrade,
                AcademicProgram = request.AcademicProgram,
                BacSeries = request.BacSeries,
                BacYear = request.BacYear
            };

            context.Students.Add(student);
            await context.SaveChangesAsync(ct);

            return student.Id;
        }
    }
}
