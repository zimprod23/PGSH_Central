using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

internal sealed class GetPeriodObjectivesQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetPeriodObjectivesQuery, IReadOnlyList<PeriodObjectiveResponse>>
{
    public async Task<Result<IReadOnlyList<PeriodObjectiveResponse>>> Handle(
        GetPeriodObjectivesQuery request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanActOnPeriodAsync(request.PeriodId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<IReadOnlyList<PeriodObjectiveResponse>>(access.Error);

        var stageId = await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.Id == request.PeriodId)
            .Select(p => (int?)p.InternshipAssignment.Cohort.StageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (stageId is null)
            return Result.Failure<IReadOnlyList<PeriodObjectiveResponse>>(StageErrors.PeriodNotFound(request.PeriodId));

        var objectives = await dbContext.StageObjectives
            .AsNoTracking()
            .Where(o => o.StageId == stageId)
            .OrderByDescending(o => o.IsMandatory)
            .ThenBy(o => o.Id)
            .Select(o => new PeriodObjectiveResponse(o.Id, o.Label, o.Description, o.Weight, o.IsMandatory))
            .ToListAsync(cancellationToken);

        return objectives;
    }
}
