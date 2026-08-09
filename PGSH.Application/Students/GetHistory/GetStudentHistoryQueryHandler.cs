using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetHistory;

internal sealed class GetStudentHistoryQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer) : IQueryHandler<GetStudentHistoryQuery, List<StudentHistoryResponse>>
{
    public async Task<Result<List<StudentHistoryResponse>>> Handle(GetStudentHistoryQuery request, CancellationToken cancellationToken)
    {
        // Same scope as the parcours and the level dossier. A timeline is not a lighter read than the
        // marks it sits beside: it carries every transfer, délocalisation, group move and failure the
        // student has ever had, and the id is guessable.
        var access = await authorizer.EnsureCanReadStudentDossierAsync(request.StudentId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<List<StudentHistoryResponse>>(access.Error);

        var studentExists = await dbContext.Students
            .AnyAsync(s => s.Id == request.StudentId, cancellationToken);

        if (!studentExists)
            return Result.Failure<List<StudentHistoryResponse>>(StudentErrors.NotFound(request.StudentId));

        var history = await dbContext.Histories// Accessing the History set
            .AsNoTracking()
            .Where(h => h.StudentId == request.StudentId)
            // Cohort is an internal planning concept — the student timeline only surfaces the
            // group change. The cohort move stays auditable via CohortMembership + AuditLog.
            .Where(h => h.HistoryData != HistoryType.CohortTransfer)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new StudentHistoryResponse(
                h.Id,
                h.HistoryData.ToString(), // Convert Enum to readable string
                h.CreatedAt,
                h.Metadata
            ))
            .ToListAsync(cancellationToken);

        return history;
    }

   
}
