using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.Create;

internal sealed class CreateRegistrationCommandHandler(
    IApplicationDbContext dbContext) : ICommandHandler<CreateRegistrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRegistrationCommand request, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students
            .Include(s => s.registrations)
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken);
        if (student is null) return Result.Failure<Guid>(StudentErrors.NotFound(request.StudentId));

        bool levelExists = await dbContext.Levels.AnyAsync(l => l.Id == request.LevelId, cancellationToken);
        if(!levelExists) return Result.Failure<Guid>(RegistrationErrors.MissingLevel);

        //Preventing a double registration
        if(student.registrations.Any(r => r.AcademicYear == request.AcademicYear))
        {
            return Result.Failure<Guid>(RegistrationErrors.DuplicateRegistration(student.Id, request.AcademicYear));
        }

        var registration = new Registration
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            AcademicYear = request.AcademicYear,
            LevelId = request.LevelId,
            Status = request.Status
        };
        var result = student.AddRegistration(registration);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return registration.Id;
    }
}
