using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Update;

internal class UpdateRegistrationCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<UpdateRegistrationCommand>
{
    public async Task<Result> Handle(UpdateRegistrationCommand request, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.Include(s => s.registrations)
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken);

        if (student is null) return Result.Failure(StudentErrors.NotFound(request.StudentId));

        var level = await dbContext.Levels
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.AcademicProgram, l.Year })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null) return Result.Failure(RegistrationErrors.MissingLevel);

        if (level.AcademicProgram != student.AcademicProgram)
            return Result.Failure(RegistrationErrors.ProgramMismatch);

        var newStartDate = await dbContext.AcademicYears
            .Where(y => y.Id == request.AcademicYearId)
            .Select(y => (DateOnly?)y.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (newStartDate is null) return Result.Failure(RegistrationErrors.MissingAcademicYear);

        var existingProgression = await dbContext.Registrations
            .Where(r => r.StudentId == request.StudentId && r.Id != request.RegistrationId)
            .Select(r => new { LevelYear = r.Level.Year, AcademicYearStart = r.AcademicYear.StartDate })
            .ToListAsync(cancellationToken);

        foreach (var existing in existingProgression)
        {
            int levelDiff = existing.LevelYear - level.Year;
            int dateDiff = existing.AcademicYearStart.CompareTo(newStartDate.Value);
            if (levelDiff != 0 && Math.Sign(levelDiff) != Math.Sign(dateDiff))
                return Result.Failure(RegistrationErrors.ChronologicalInconsistency);
        }

        FailureReasons? failureReasons = request.FailureDescription is not null
            ? new FailureReasons(request.FailureDescription, request.FailureNotes ?? new(),request.Cheat ?? false)
            : null;
        var result = student.UpdateRegistration(
            request.RegistrationId,
            request.Status,
            request.AcademicYearId,
            request.LevelId,
            failureReasons);

        if (result.IsFailure) return result;
        
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
