using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Attendance.Record;

internal sealed class RecordAttendanceCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<RecordAttendanceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordAttendanceCommand request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanRecordAttendanceAsync(request.ServicePeriodId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<Guid>(access.Error);

        var existing = await dbContext.AttendanceRecords
            .FirstOrDefaultAsync(
                a => a.ServicePeriodId == request.ServicePeriodId && a.Date == request.Date,
                cancellationToken);

        if (existing is not null)
        {
            existing.Status = request.Status;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(existing.Id);
        }

        var record = new AttendanceRecord
        {
            Id              = Guid.NewGuid(),
            ServicePeriodId = request.ServicePeriodId,
            Date            = request.Date,
            Status          = request.Status,
        };

        dbContext.AttendanceRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(record.Id);
    }
}
