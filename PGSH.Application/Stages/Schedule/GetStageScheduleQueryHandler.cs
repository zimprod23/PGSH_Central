using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule;

internal sealed class GetStageScheduleQueryHandler(
    IApplicationDbContext dbContext,
    ServiceOccupancyCalculator occupancyCalculator)
    : IQueryHandler<GetStageScheduleQuery, StageScheduleResponse>
{
    public async Task<Result<StageScheduleResponse>> Handle(
        GetStageScheduleQuery request, CancellationToken cancellationToken)
    {
        var slots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.StageId == request.StageId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new StageSlotResponse(s.Id, s.PeriodNumber, s.Label, s.StartDate, s.EndDate))
            .ToListAsync(cancellationToken);

        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId
                     && (request.AcademicYearId == null || c.AcademicGroup.AcademicYearId == request.AcademicYearId))
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
                    ServiceCapacity = a.Service.Capacity,
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

        var cohortRows = cohorts.Select(c =>
        {
            var cellsBySlot = c.SlotAssignments.ToDictionary(a => a.StageSlotId);

            var cells = slots.Select(slot =>
            {
                if (!cellsBySlot.TryGetValue(slot.Id, out var a))
                    return (SlotCellResponse?)null;

                int occupied = occupancy.LoadOn(a.ServiceId, slot.StartDate, slot.EndDate);

                return new SlotCellResponse(a.Id, a.StageSlotId, a.ServiceId, a.ServiceName, a.HospitalName, a.ServiceCapacity, occupied);
            }).ToList();

            return new CohortScheduleRow(c.Id, c.Label, c.AcademicGroupId, c.AcademicGroupLabel, c.RotationGroup, c.StudentCount, c.IsSchedulePublished, cells);
        }).ToList();

        return new StageScheduleResponse(request.StageId, slots, cohortRows);
    }
}
