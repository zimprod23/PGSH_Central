using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.GetCurriculum;

internal sealed class GetCurriculumQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCurriculumQuery, CurriculumResponse>
{
    public async Task<Result<CurriculumResponse>> Handle(
        GetCurriculumQuery request, CancellationToken cancellationToken)
    {
        var curriculum = await dbContext.Curriculums
            .AsNoTracking()
            .Where(c => c.LevelId == request.LevelId && c.CnpnVersionId == request.CnpnVersionId)
            .Select(c => new CurriculumResponse(
                c.Id,
                c.LevelId,
                c.Level.Label,
                c.CnpnVersionId,
                c.CnpnVersion.Code,
                c.CnpnVersion.Label,
                c.CnpnVersion.TotalYears,
                c.Reference,
                c.Stages
                    .OrderBy(s => s.Stage.Name)
                    .Select(s => new CurriculumStageResponse(
                        s.StageId,
                        s.Stage.Name,
                        s.Coefficient,
                        s.DurationInDays))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return curriculum is null
            ? Result.Failure<CurriculumResponse>(
                CurriculumErrors.NotFound(request.LevelId, request.CnpnVersionId))
            : curriculum;
    }
}
