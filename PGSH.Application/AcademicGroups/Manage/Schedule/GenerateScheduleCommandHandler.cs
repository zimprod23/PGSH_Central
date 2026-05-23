using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage.Schedule;

internal sealed class GenerateScheduleCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<GenerateScheduleCommand, BulkResponse<int, int>>
{
    private readonly Dictionary<int, Dictionary<DateOnly, int>> _occupancy = new();

    public async Task<Result<BulkResponse<int, int>>> Handle(
        GenerateScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Include(g => g.Registrations)
            .ToListAsync(cancellationToken);

        var services = await dbContext.Services
            .Where(s => request.AvailableServiceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s, cancellationToken);

        await HydrateOccupancyAsync(cancellationToken);

        var results = new List<BulkItemResult<int, int>>();
        int groupIndex = 0;

        foreach (var stageReq in request.Stages)
        {
            int duration  = stageReq.RotationDurationDays;
            int rotations = Math.Max(1, stageReq.NumberOfRotations);

            // Create or reuse StageSlots for this stage (one per period, shared across all cohorts)
            var stageSlots = new List<StageSlot>();
            for (int r = 0; r < rotations; r++)
            {
                DateOnly start = stageReq.GlobalStartDate.AddDays(r * duration);
                DateOnly end   = stageReq.GlobalStartDate.AddDays(((r + 1) * duration) - 1);

                var slot = await dbContext.StageSlots
                    .FirstOrDefaultAsync(s => s.StageId == stageReq.StageId && s.PeriodNumber == r + 1, cancellationToken);

                if (slot is null)
                {
                    slot = new StageSlot
                    {
                        StageId      = stageReq.StageId,
                        PeriodNumber = r + 1,
                        StartDate    = start,
                        EndDate      = end,
                    };
                    dbContext.StageSlots.Add(slot);
                }

                stageSlots.Add(slot);
            }

            await dbContext.SaveChangesAsync(cancellationToken); // flush slot IDs

            foreach (var group in groups)
            {
                var cohort = new Cohort
                {
                    StageId         = stageReq.StageId,
                    AcademicGroupId = group.Id,
                    Label           = $"Cohort-{group.Id}-Stage{stageReq.StageId}",
                };

                bool allPlaced   = true;
                var  tempSlotSvc = new List<(StageSlot Slot, int ServiceId)>();

                for (int r = 0; r < rotations; r++)
                {
                    var slot      = stageSlots[r];
                    int serviceId = FindAvailableService(
                        request.AvailableServiceIds, groupIndex, r,
                        slot.StartDate, slot.EndDate, group.Registrations.Count, services);

                    if (serviceId == -1)
                    {
                        results.Add(new BulkItemResult<int, int>(group.Id, 0,
                            Error.Conflict("Schedule.Capacity", $"No available service for group {group.Id}")));
                        allPlaced = false;
                        break;
                    }

                    tempSlotSvc.Add((slot, serviceId));
                    Track(serviceId, slot.StartDate, slot.EndDate, group.Registrations.Count);
                }

                if (allPlaced)
                {
                    foreach (var (slot, svcId) in tempSlotSvc)
                    {
                        cohort.SlotAssignments.Add(new CohortSlotAssignment
                        {
                            StageSlot = slot,
                            ServiceId = svcId,
                        });
                    }

                    foreach (var reg in group.Registrations)
                    {
                        var assignment = new InternshipAssignment
                        {
                            Id             = Guid.NewGuid(),
                            RegistrationId = reg.Id,
                            Cohort         = cohort,
                        };

                        assignment.MembershipHistory.Add(new CohortMembership
                        {
                            Id        = Guid.NewGuid(),
                            StartDate = stageReq.GlobalStartDate,
                            Cohort    = cohort,
                        });

                        dbContext.InternshipAssignments.Add(assignment);
                    }

                    dbContext.Cohorts.Add(cohort);
                    results.Add(new BulkItemResult<int, int>(group.Id, group.Id, null));
                }

                groupIndex++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<int, int>(
            results,
            groups.Count,
            results.Count(r => r.IsSuccess),
            results.Count(r => !r.IsSuccess)));
    }

    private int FindAvailableService(
        List<int> available, int groupIdx, int rotIdx,
        DateOnly start, DateOnly end, int studentCount,
        Dictionary<int, Service> services)
    {
        for (int attempt = 0; attempt < available.Count; attempt++)
        {
            int id = available[(groupIdx + rotIdx + attempt) % available.Count];
            if (CanFit(id, start, end, studentCount, services[id].Capacity))
                return id;
        }
        return -1;
    }

    private async Task HydrateOccupancyAsync(CancellationToken ct)
    {
        var existing = await dbContext.CohortSlotAssignments
            .Select(a => new
            {
                a.ServiceId,
                Start    = a.StageSlot.StartDate,
                End      = a.StageSlot.EndDate,
                Students = a.Cohort.Assignments.Count,
            })
            .ToListAsync(ct);

        foreach (var a in existing)
            Track(a.ServiceId, a.Start, a.End, a.Students);
    }

    private bool CanFit(int serviceId, DateOnly start, DateOnly end, int count, int capacity)
    {
        if (!_occupancy.TryGetValue(serviceId, out var days)) return count <= capacity;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (days.GetValueOrDefault(d) + count > capacity) return false;
        return true;
    }

    private void Track(int serviceId, DateOnly start, DateOnly end, int count)
    {
        if (!_occupancy.ContainsKey(serviceId)) _occupancy[serviceId] = new();
        var days = _occupancy[serviceId];
        for (var d = start; d <= end; d = d.AddDays(1))
            days[d] = days.GetValueOrDefault(d) + count;
    }
}
