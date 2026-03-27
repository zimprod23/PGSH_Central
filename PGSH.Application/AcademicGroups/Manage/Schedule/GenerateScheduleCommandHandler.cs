using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage.Schedule;

internal sealed class GenerateScheduleCommandHandler(
    IApplicationDbContext dbContext)
    : ICommandHandler<GenerateScheduleCommand, BulkResponse<int, Guid>>
{
    private readonly Dictionary<int, Dictionary<DateOnly, int>> _occupancyTracker = new();

    public async Task<Result<BulkResponse<int, Guid>>> Handle(
        GenerateScheduleCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Prepare Metadata
        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Include(g => g.Registrations)
            .ToListAsync(cancellationToken);

        var services = await dbContext.Services
            .Where(s => request.AvailableServiceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s);

        // 2. GLOBAL HYDRATION: Fixed to look through ServicePeriods
        await HydrateGlobalOccupancyAsync(cancellationToken);

        var itemResults = new List<BulkItemResult<int, Guid>>();
        int groupIndex = 0;

        foreach (var stageReq in request.Stages)
        {
            DateOnly stageStart = stageReq.GlobalStartDate;
            int rotations = Math.Max(1, stageReq.NumberOfRotations);
            int duration = stageReq.RotationDurationDays;

            foreach (var group in groups)
            {
                // Create the Cohort first (It holds the StageId and Label)
                var cohort = new Cohort
                {
                    StageId = stageReq.StageId,
                    AcademicGroupId = group.Id,
                    Label = $"Cohort-{group.Id}-Stage{stageReq.StageId}"
                };

                bool allRotationsPlaced = true;
                var tempPeriods = new List<CohortRotationTemplate>();

                for (int r = 0; r < rotations; r++)
                {
                    DateOnly pStart = stageStart.AddDays(r * duration);
                    DateOnly pEnd = stageStart.AddDays(((r + 1) * duration) - 1);

                    int serviceId = FindAvailableService(request.AvailableServiceIds, groupIndex, r, pStart, pEnd, group.Registrations.Count, services);

                    if (serviceId == -1)
                    {
                        itemResults.Add(new BulkItemResult<int, Guid>(group.Id, Guid.Empty,
                            Error.Conflict("Capacity.Full", $"No service available for Group {group.Id}")));
                        allRotationsPlaced = false;
                        break;
                    }

                    tempPeriods.Add(new CohortRotationTemplate
                    {
                        ServiceId = serviceId,
                        PlannedStart = pStart,
                        PlannedEnd = pEnd,
                        SequenceOrder = r + 1
                    });

                    Track(serviceId, pStart, pEnd, group.Registrations.Count);
                }

                if (allRotationsPlaced)
                {
                    cohort.RotationTemplates = tempPeriods;

                    foreach (var reg in group.Registrations)
                    {
                        var assignment = new InternshipAssignment
                        {
                            Id = Guid.NewGuid(),
                            RegistrationId = reg.Id,
                            CurrentCohortId = cohort.Id, // EF will handle the temporary ID
                            Status = InternshipStatus.Planned,
                            //Result = StageAssignmentResult.NonÉvalué
                        };

                        // Create ServicePeriods from the Template
                        foreach (var t in cohort.RotationTemplates)
                        {
                            assignment.ServicePeriods.Add(new ServicePeriod
                            {
                                ServiceId = t.ServiceId,
                                StartDate = t.PlannedStart,
                                EndDate = t.PlannedEnd,
                                IsComplete = false
                            });
                        }

                        // Add initial membership history
                        assignment.MembershipHistory.Add(new CohortMembership
                        {
                            Id = Guid.NewGuid(),
                            StartDate = stageStart,
                            // Cohort link will be established via InternshipAssignment
                        });

                        dbContext.InternshipAssignments.Add(assignment);
                    }
                    dbContext.Cohorts.Add(cohort);
                    itemResults.Add(new BulkItemResult<int, Guid>(group.Id, Guid.NewGuid(), null));
                }
                groupIndex++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new BulkResponse<int, Guid>(itemResults, groups.Count, itemResults.Count(x => x.IsSuccess), itemResults.Count(x => !x.IsSuccess)));
    }

    private int FindAvailableService(List<int> availableIds, int groupIdx, int rotationIdx, DateOnly start, DateOnly end, int studentCount, Dictionary<int, Service> services)
    {
        for (int attempts = 0; attempts < availableIds.Count; attempts++)
        {
            int idx = (groupIdx + rotationIdx + attempts) % availableIds.Count;
            int candidateId = availableIds[idx];
            if (CanFit(candidateId, start, end, studentCount, services[candidateId].Capacity)) return candidateId;
        }
        return -1;
    }

    private async Task HydrateGlobalOccupancyAsync(CancellationToken ct)
    {
        // Fix: Use ServicePeriod to track occupancy across all assignments
        var existing = await dbContext.ServicePeriods
            .Select(sp => new {
                sp.ServiceId,
                sp.StartDate,
                sp.EndDate,
                // We need the group size. If we don't have it on ServicePeriod, 
                // we join back to Registration or just count individual records.
                StudentCount = 1
            })
            .ToListAsync(ct);

        foreach (var p in existing)
        {
            Track(p.ServiceId, p.StartDate, p.EndDate, p.StudentCount);
        }
    }

    private bool CanFit(int serviceId, DateOnly start, DateOnly end, int count, int capacity)
    {
        if (!_occupancyTracker.ContainsKey(serviceId)) return count <= capacity;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (_occupancyTracker[serviceId].GetValueOrDefault(d, 0) + count > capacity) return false;
        return true;
    }

    private void Track(int serviceId, DateOnly start, DateOnly end, int count)
    {
        if (!_occupancyTracker.ContainsKey(serviceId)) _occupancyTracker[serviceId] = new();
        for (var d = start; d <= end; d = d.AddDays(1))
            _occupancyTracker[serviceId][d] = _occupancyTracker[serviceId].GetValueOrDefault(d, 0) + count;
    }
}