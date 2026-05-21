using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Create;

internal sealed class CreateRegistrationCommandHandler(
    IApplicationDbContext dbContext) : ICommandHandler<CreateRegistrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRegistrationCommand request, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (student is null) return Result.Failure<Guid>(StudentErrors.NotFound(request.StudentId));

        var level = await dbContext.Levels
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id, l.AcademicProgram, l.Year })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null) return Result.Failure<Guid>(RegistrationErrors.MissingLevel);

        if (level.AcademicProgram != student.AcademicProgram)
            return Result.Failure<Guid>(RegistrationErrors.ProgramMismatch);

        var newStartDate = await dbContext.AcademicYears
            .Where(y => y.Id == request.AcademicYearId)
            .Select(y => (DateOnly?)y.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (newStartDate is null) return Result.Failure<Guid>(RegistrationErrors.MissingAcademicYear);

        var existingProgression = await dbContext.Registrations
            .Where(r => r.StudentId == request.StudentId)
            .Select(r => new { LevelYear = r.Level.Year, AcademicYearStart = r.AcademicYear.StartDate })
            .ToListAsync(cancellationToken);

        foreach (var existing in existingProgression)
        {
            int levelDiff = existing.LevelYear - level.Year;
            int dateDiff = existing.AcademicYearStart.CompareTo(newStartDate.Value);
            if (levelDiff != 0 && Math.Sign(levelDiff) != Math.Sign(dateDiff))
                return Result.Failure<Guid>(RegistrationErrors.ChronologicalInconsistency);
        }

        var alreadyRegistered = await dbContext.Registrations.AnyAsync(
            r => r.StudentId == request.StudentId && r.AcademicYearId == request.AcademicYearId,
            cancellationToken);

        if (alreadyRegistered) return Result.Failure<Guid>(RegistrationErrors.DuplicateRegistration(request.StudentId, request.AcademicYearId));


        var registration = new Registration
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            AcademicYearId = request.AcademicYearId,
            LevelId = request.LevelId,
            Status = request.Status
        };

        registration.Raise(new StudentRegisteredDomainEvent(
            registration.Id,
            request.StudentId,
            request.LevelId,
            request.AcademicYearId));

        dbContext.Registrations.Add(registration);

        await dbContext.SaveChangesAsync(cancellationToken);
        return registration.Id;
    }
}
