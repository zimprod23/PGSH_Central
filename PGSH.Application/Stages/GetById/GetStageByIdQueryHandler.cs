using PGSH.Application.Stages.Levels;
﻿using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Students.GetById;
using PGSH.Application.Stages.AllowedServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetById;

internal sealed class GetStageByIdQueryHandler(
    IApplicationDbContext dbContext) : IQueryHandler<GetStageByIdQuery, StageResponse>
{
    public async Task<Result<StageResponse>> Handle(GetStageByIdQuery request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
                        .AsNoTracking()
                        .Include(s => s.Level)
                        .Include(s => s.Objectives)
                        .Include(s => s.AllowedServices)
                        .Where(s => s.Id == request.StageId)
                        .Select(s => new StageResponse(
                            s.Id,
                            s.Name,
                            s.Coefficient,
                            s.Description,
                            s.DurationInDays,
                            s.RotationMode,
                            new LevelResponse(
                                s.Level.Id,
                                s.Level.Label,
                                s.Level.Year,
                                s.Level.AcademicProgram.ToString()
                                ),
                            s.Objectives
                                .OrderByDescending(o => o.Weight)
                                .Select(o => new StageObjectiveResponse(
                                    o.Label,
                                    o.Description,
                                    o.Weight,
                                    o.IsMandatory
                                    ))
                                .ToArray(),
                            s.AllowedServices
                                .Select(svc => new AllowedServiceSummary(svc.Id, svc.Name, svc.Hospital.Name, 0))
                                .ToArray()
                            ))
                        .FirstOrDefaultAsync(cancellationToken);

        if(stage is null) return Result.Failure<StageResponse>(StageErrors.NotFound(request.StageId));

        // ⚠ Read by a second flat query keyed on the stage id, never as a collection inside the row
        // projection: the rank lives on the join, and a projected join row is a computed element
        // carrying no key — the shape Npgsql refuses. Pinned by SqlTranslationTests.
        var rankByService = await ServiceRankWriter.RanksQuery(dbContext, request.StageId)
            .ToDictionaryAsync(x => x.ServiceId, x => x.Rank, cancellationToken);

        // Returned in the order the rotation is actually walked, so the position shown beside a
        // service is the position it holds. Unranked rows fall to the end on their id, which is the
        // pre-Rank behaviour.
        return stage with
        {
            AllowedServices = [.. stage.AllowedServices
                .Select(svc => svc with { Rank = rankByService.GetValueOrDefault(svc.Id) })
                .OrderBy(svc => ServiceRotationOrder.SortKeyOf(svc.Rank))
                .ThenBy(svc => svc.Id)],
        };
    }
}
