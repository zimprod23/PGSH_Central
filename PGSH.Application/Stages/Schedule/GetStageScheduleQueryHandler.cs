using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule;

internal sealed class GetStageScheduleQueryHandler(IApplicationDbContext dbContext)
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
            .Where(c => c.StageId == request.StageId)
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

        // Sum planned student counts per (slot, service) across all cohorts — published or not
        var slotServiceOccupancy = cohorts
            .SelectMany(c => c.SlotAssignments.Select(a => new { a.StageSlotId, a.ServiceId, c.StudentCount }))
            .GroupBy(a => new { a.StageSlotId, a.ServiceId })
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.StudentCount));

        var slotIds = slots.Select(s => s.Id).ToHashSet();

        var cohortRows = cohorts.Select(c =>
        {
            var cellsBySlot = c.SlotAssignments.ToDictionary(a => a.StageSlotId);

            var cells = slots.Select(slot =>
            {
                if (!cellsBySlot.TryGetValue(slot.Id, out var a))
                    return (SlotCellResponse?)null;

                var key = new { StageSlotId = slot.Id, a.ServiceId };
                int occupied = slotServiceOccupancy.GetValueOrDefault(key, 0);

                return new SlotCellResponse(a.Id, a.StageSlotId, a.ServiceId, a.ServiceName, a.HospitalName, a.ServiceCapacity, occupied);
            }).ToList();

            return new CohortScheduleRow(c.Id, c.Label, c.AcademicGroupId, c.AcademicGroupLabel, c.RotationGroup, c.StudentCount, c.IsSchedulePublished, cells);
        }).ToList();

        return new StageScheduleResponse(request.StageId, slots, cohortRows);
    }
}
