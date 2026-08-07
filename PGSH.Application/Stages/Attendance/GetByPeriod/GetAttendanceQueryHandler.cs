using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Attendance.GetByPeriod;

internal sealed class GetAttendanceQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetAttendanceQuery, List<AttendanceResponse>>
{
    public async Task<Result<List<AttendanceResponse>>> Handle(
        GetAttendanceQuery request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanReadAttendanceAsync(request.ServicePeriodId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<List<AttendanceResponse>>(access.Error);

        var records = await dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(a => a.ServicePeriodId == request.ServicePeriodId)
            .OrderBy(a => a.Date)
            .Select(a => new AttendanceResponse(a.Id, a.Date, a.Status.ToString()))
            .ToListAsync(cancellationToken);

        return Result.Success(records);
    }
}
