using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.SharedKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.Delete;

internal sealed class DeleteRegistrationCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteRegistrationCommand>
{
    public async Task<Result> Handle(DeleteRegistrationCommand request, CancellationToken ct)
    {
        var student = await dbContext.Students
            .Include(s => s.registrations)
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);

        if (student is null) return Result.Failure(StudentErrors.NotFound(request.StudentId));

        var result = student.RemoveRegistration(request.RegistrationId);

        if (result.IsFailure) return result;

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }
}