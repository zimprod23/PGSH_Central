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

namespace PGSH.Application.Students.Delete
{
    internal sealed class DeleteStudentCommandHandler(IApplicationDbContext context)
    : ICommandHandler<DeleteStudentCommand>
    {
        public async Task<Result> Handle(DeleteStudentCommand request, CancellationToken ct)
        {
            var student = await context.Students
                .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);

            if (student is null)
            {
                return Result.Failure(StudentErrors.NotFound(request.StudentId));
            }

            context.Students.Remove(student);
            await context.SaveChangesAsync(ct);

            return Result.Success();
        }
    }
}
