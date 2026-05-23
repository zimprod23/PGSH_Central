using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Attendance.Generate;

internal sealed class GenerateAttendanceCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<GenerateAttendanceCommand, int>
{
    public async Task<Result<int>> Handle(
        GenerateAttendanceCommand request, CancellationToken cancellationToken)
    {
        var period = await dbContext.ServicePeriods
            .Where(p => p.Id == request.ServicePeriodId)
            .Select(p => new { p.StartDate, p.EndDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (period is null)
            return Result.Failure<int>(StageErrors.PeriodNotFound(request.ServicePeriodId));

        bool alreadyGenerated = await dbContext.AttendanceRecords
            .AnyAsync(a => a.ServicePeriodId == request.ServicePeriodId, cancellationToken);

        if (alreadyGenerated)
            return Result.Failure<int>(StageErrors.AttendanceAlreadyGenerated);

        var records = new List<AttendanceRecord>();
        for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
        {
            records.Add(new AttendanceRecord
            {
                Id              = Guid.NewGuid(),
                ServicePeriodId = request.ServicePeriodId,
                Date            = date,
                Status          = AttendanceStatus.Present,
            });
        }

        dbContext.AttendanceRecords.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(records.Count);
    }
}
