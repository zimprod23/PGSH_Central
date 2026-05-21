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
            .Select(l => new { l.Id, l.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null) return Result.Failure<Guid>(RegistrationErrors.MissingLevel);

        if (level.AcademicProgram != student.AcademicProgram)
            return Result.Failure<Guid>(RegistrationErrors.ProgramMismatch);

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
