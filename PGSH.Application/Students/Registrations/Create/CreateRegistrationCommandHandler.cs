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
        var studentExists = await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken);
        if (!studentExists) return Result.Failure<Guid>(StudentErrors.NotFound(request.StudentId));

        bool levelExists = await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken);
        if (!levelExists) return Result.Failure<Guid>(RegistrationErrors.MissingLevel);

        var alreadyRegistered = await dbContext.Registrations.AnyAsync(s => s.StudentId == request.StudentId && s.AcademicYear == request.AcademicYear, cancellationToken);
        if (alreadyRegistered) return Result.Failure<Guid>(RegistrationErrors.DuplicateRegistration(request.StudentId, request.AcademicYear));


        var registration = new Registration
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            AcademicYear = request.AcademicYear,
            LevelId = request.LevelId,
            Status = request.Status
        };

        registration.Raise(new StudentRegisteredDomainEvent(
            registration.Id,
            request.StudentId,
            request.LevelId,
            request.AcademicYear));

        dbContext.Registrations.Add(registration);

        await dbContext.SaveChangesAsync(cancellationToken);
        return registration.Id;
    }
}
