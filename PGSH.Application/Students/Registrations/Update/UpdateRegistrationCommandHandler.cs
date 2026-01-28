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

        FailureReasons? failureReasons = request.FailureDescription is not null
            ? new FailureReasons(request.FailureDescription, request.FailureNotes ?? new(),request.Cheat ?? false)
            : null;
        var result = student.UpdateRegistration(
            request.RegistrationId,
            request.Status,
            request.AcademicYear,
            request.LevelId,
            failureReasons);

        if (result.IsFailure) return result;
        
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
