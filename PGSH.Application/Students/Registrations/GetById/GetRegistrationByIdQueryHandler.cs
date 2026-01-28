using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.GetById;

internal sealed class GetRegistrationByIdQueryHandler(IApplicationDbContext dbContext) : IQueryHandler<GetRegistrationByIdQuery, RegistrationResponse>
{
    public async Task<Result<RegistrationResponse>> Handle(GetRegistrationByIdQuery request, CancellationToken cancellationToken)
    {
        var registration = await dbContext.Registrations
            .Where(r => r.Id == request.Id)
            .Select(r => new RegistrationResponse(
                r.Id,
                r.StudentId,
                $"{r.Student.FirstName} {r.Student.LastName}", // Joining student name efficiently
                r.AcademicYear,
                r.LevelId,
                r.Status,
                r.failureReasons != null ? r.failureReasons.Description : null,
                r.failureReasons != null ? r.failureReasons.Notes : new List<string>(),
                r.failureReasons != null && r.failureReasons.Cheat
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<RegistrationResponse>(RegistrationErrors.NotFound(request.Id));
        }

        return registration;
    }
}
