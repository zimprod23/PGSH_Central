using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule;

internal sealed class GetStageScheduleQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ServiceOccupancyCalculator occupancyCalculator,
    ServiceIntakeCalculator intakeCalculator)
    : IQueryHandler<GetStageScheduleQuery, StageScheduleResponse>
{
    public async Task<Result<StageScheduleResponse>> Handle(
        GetStageScheduleQuery request, CancellationToken cancellationToken)
    {
        // Both axes of the grid are year-scoped: slots carry the year's dates, and a cohort exists
        // per (stage, group) with groups per year — unscoped, "Chirurgie" returned the 681 cohorts of
        // every year it ever ran.
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<StageScheduleResponse>(year.Error);

        int academicYearId = year.Value;

        // The grid is one stage, so one level: every quota shown below is that level's.
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.LevelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (stage is null)
            return Result.Failure<StageScheduleResponse>(StageErrors.NotFound(request.StageId));

        int levelId = stage.LevelId;

        var slots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == request.StageId && s.AcademicYearId == academicYearId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new StageSlotResponse(s.Id, s.PeriodNumber, s.Label, s.StartDate, s.EndDate))
            .ToListAsync(cancellationToken);

        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId
                     && c.AcademicGroup.AcademicYearId == academicYearId)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Label,
                c.AcademicGroupId,
                AcademicGroupLabel  = c.AcademicGroup.Label,
                RotationGroup       = c.AcademicGroup.RotationGroup,
                StudentCount       = c.Assignments.Count,
                IsSchedulePublished = c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)),
                SlotAssignments    = c.SlotAssignments.Select(a => new
                {
                    a.Id,
                    a.StageSlotId,
                    a.ServiceId,
                    ServiceName   = a.Service.Name,
                    HospitalName  = a.Service.Hospital.Name,
                }).ToList(),
            })
            .ToListAsync(cancellationToken);

        // Occupancy is measured globally: a service's load in a slot is every student
        // (any stage/partition) whose own slot window overlaps this one. This surfaces the
        // cross-stage case — the same physical service shared by two partitions running
        // different stages over overlapping dates shows its combined load, not a per-stage half.
        var serviceIds = cohorts
            .SelectMany(c => c.SlotAssignments.Select(a => a.ServiceId))
            .Distinct()
            .ToList();

        var occupancy = await occupancyCalculator.BuildAsync(serviceIds, cancellationToken);
        var intake = await intakeCalculator.BuildAsync(serviceIds, cancellationToken);

        var cohortRows = cohorts.Select(c =>
        {
            var cellsBySlot = c.SlotAssignments.ToDictionary(a => a.StageSlotId);

            var cells = slots.Select(slot =>
            {
                if (!cellsBySlot.TryGetValue(slot.Id, out var a))
                    return (SlotCellResponse?)null;

                // The load has to be counted the same way the governing limit is written: against
                // this promotion when the service grants it a quota, against everybody when the
                // service has one number for all comers.
                bool isLevelQuota = intake.HasLevelRestrictions(a.ServiceId);

                int occupied = isLevelQuota
                    ? occupancy.LoadOn(a.ServiceId, levelId, slot.StartDate, slot.EndDate)
                    : occupancy.LoadOn(a.ServiceId, slot.StartDate, slot.EndDate);

                return new SlotCellResponse(
                    a.Id, a.StageSlotId, a.ServiceId, a.ServiceName, a.HospitalName,
                    intake.CapacityFor(a.ServiceId, levelId), occupied,
                    isLevelQuota,
                    intake.Admits(a.ServiceId, levelId));
            }).ToList();

            return new CohortScheduleRow(c.Id, c.Label, c.AcademicGroupId, c.AcademicGroupLabel, c.RotationGroup, c.StudentCount, c.IsSchedulePublished, cells);
        }).ToList();

        return new StageScheduleResponse(request.StageId, slots, cohortRows);
    }
}
