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
            // ⚠ Every comparison is guarded on the *request* value being present. CNE and CIN are
            // both optional, and `null == null` is true in memory while it is NULL — i.e. false — in
            // SQL, so an unguarded predicate reports "CNE déjà utilisé" against the next student
            // without one under the in-memory provider and passes silently on PostgreSQL. The
            // uniqueness indexes are filtered on IS NOT NULL for the same reason: an absent
            // identifier collides with nothing.
            string? cne = StudentIdentifierRules.NormalizeCne(request.CNE);

            var existing = await context.Students
                .Where(s => (cne != null && s.CNE == cne) ||
                            s.Email == request.Email ||
                            (request.CIN != null && s.CIN == request.CIN) ||
                            s.Appogee == request.Appogee)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                var culprit = cne != null && existing.CNE == cne ? ("CNE", cne) :
                              existing.Email == request.Email ? ("Email", request.Email) :
                              existing.Appogee == request.Appogee ? ("Appogee", request.Appogee) :
                              ("CIN", request.CIN!);

                return Result.Failure<Guid>(StudentErrors.Conflict(culprit.Item1, culprit.Item2));
            }

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
                CNE = cne,
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
