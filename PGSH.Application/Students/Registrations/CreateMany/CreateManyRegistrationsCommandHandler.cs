using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.CreateMany;

internal sealed class CreateManyRegistrationsCommandHandler(
    IApplicationDbContext dbContext,
    RegistrationCnpnStamper stamper,
    FinalYearGuard finalYear)
    : ICommandHandler<CreateManyRegistrationsCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        CreateManyRegistrationsCommand request,
        CancellationToken cancellationToken)
    {
        bool levelExists = await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken);
        if (!levelExists) return Result.Failure<BulkResponse<Guid, Guid>>(RegistrationErrors.MissingLevel);

        var existingRegistrationIds = await dbContext.Registrations
            .Where(r => r.AcademicYearId == request.AcademicYearId && request.StudentIds.Contains(r.StudentId))
            .Select(r => r.StudentId)
            .ToListAsync(cancellationToken);

        var validStudentIds = await dbContext.Students
            .Where(s => request.StudentIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<Guid>(existingRegistrationIds);
        var validSet = new HashSet<Guid>(validStudentIds);

        // One pass for the whole batch, like the stamp below. Asked inside the loop it pulled the
        // student's entire cursus per student — ~2 800 round-trips to enrol a promotion of 700.
        // Only the students still in the running are handed over: the rest are already refused, and
        // an id nobody recognises has no cursus to read.
        var gated = request.StudentIds
            .Distinct()
            .Where(id => validSet.Contains(id) && !existingSet.Contains(id))
            .ToList();

        var finalYearRefusals = await finalYear.EnsureMayEnterManyAsync(
            gated, request.LevelId, request.AcademicYearId, cancellationToken);

        var itemResults = new List<BulkItemResult<Guid, Guid>>();
        var newRegistrations = new List<Registration>();

        foreach (var studentId in request.StudentIds.Distinct())
        {
            // Error: Student doesn't exist in the system
            if (!validSet.Contains(studentId))
            {
                itemResults.Add(new BulkItemResult<Guid, Guid>(studentId, default, StudentErrors.NotFound(studentId)));
                continue;
            }

            // Error: Student is already registered for this specific academic year
            if (existingSet.Contains(studentId))
            {
                itemResults.Add(new BulkItemResult<Guid, Guid>(studentId, default, RegistrationErrors.DuplicateRegistration(studentId, request.AcademicYearId)));
                continue;
            }

            // Same gate as the single-registration path, and still answered per student — whether
            // this is somebody's last year is a question about his own text, and the answer differs
            // inside one promotion since 1650.25. Only the *asking* is batched.
            if (finalYearRefusals.TryGetValue(studentId, out var blocked))
            {
                itemResults.Add(new BulkItemResult<Guid, Guid>(studentId, default, blocked));
                continue;
            }

            // Success: Create the registration entity
            var reg = new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                AcademicYearId = request.AcademicYearId,
                LevelId = request.LevelId,
                Status = request.Status,
                RegistrationDate = DateTime.UtcNow
            };

            // Trigger domain events for downstream logic (like your "Lexi" document system)
            reg.Raise(new StudentRegisteredDomainEvent(reg.Id, studentId, request.LevelId, request.AcademicYearId));

            newRegistrations.Add(reg);
            itemResults.Add(new BulkItemResult<Guid, Guid>(studentId, reg.Id, null));
        }

        if (newRegistrations.Any())
        {
            dbContext.Registrations.AddRange(newRegistrations);

            // One pass for the whole batch: the population's stamps, prior texts and the year's
            // effectivity rules are three lookups, not four per student.
            await stamper.StampAsync(newRegistrations, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var response = new BulkResponse<Guid, Guid>(
            itemResults,
            request.StudentIds.Count,
            newRegistrations.Count,
            itemResults.Count - newRegistrations.Count
        );

        return Result.Success(response);
    }
}