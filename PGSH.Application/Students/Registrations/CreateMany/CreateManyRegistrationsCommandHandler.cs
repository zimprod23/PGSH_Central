using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.CreateMany;

internal sealed class CreateManyRegistrationsCommandHandler(
    IApplicationDbContext dbContext)
    : ICommandHandler<CreateManyRegistrationsCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        CreateManyRegistrationsCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Initial Infrastructure check
        bool levelExists = await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken);
        if (!levelExists) return Result.Failure<BulkResponse<Guid, Guid>>(RegistrationErrors.MissingLevel);

        // 2. Optimization: Bulk fetch existing data for O(1) in-memory checks
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

        var itemResults = new List<BulkItemResult<Guid, Guid>>();
        var newRegistrations = new List<Registration>();

        // 3. Process each Student ID
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

        // 4. Atomic Save for the valid subset
        if (newRegistrations.Any())
        {
            dbContext.Registrations.AddRange(newRegistrations);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // 5. Construct the Generic Bulk Response
        var response = new BulkResponse<Guid, Guid>(
            itemResults,
            request.StudentIds.Count,
            newRegistrations.Count,
            itemResults.Count - newRegistrations.Count
        );

        return Result.Success(response);
    }
}