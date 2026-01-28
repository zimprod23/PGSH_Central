using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetHistory;

internal sealed class GetStudentHistoryQueryHandler(
    IApplicationDbContext dbContext) : IQueryHandler<GetStudentHistoryQuery, List<StudentHistoryResponse>>
{
    public async Task<Result<List<StudentHistoryResponse>>> Handle(GetStudentHistoryQuery request, CancellationToken cancellationToken)
    {
        // 1. Check if student exists
        var studentExists = await dbContext.Students
            .AnyAsync(s => s.Id == request.StudentId, cancellationToken);

        if (!studentExists)
            return Result.Failure<List<StudentHistoryResponse>>(StudentErrors.NotFound(request.StudentId));

        // 2. Query the dedicated History table
        var history = await dbContext.Histories// Accessing the History set
            .AsNoTracking()
            .Where(h => h.StudentId == request.StudentId)
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
