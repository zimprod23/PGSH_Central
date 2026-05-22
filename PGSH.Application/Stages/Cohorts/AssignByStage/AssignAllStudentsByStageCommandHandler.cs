using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.AssignByStage;

internal sealed class AssignAllStudentsByStageCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignAllStudentsByStageCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        AssignAllStudentsByStageCommand request, CancellationToken cancellationToken)
    {
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId)
            .Select(c => new { c.Id, c.AcademicGroupId, LevelId = c.Stage.LevelId })
            .ToListAsync(cancellationToken);

        if (cohorts.Count == 0)
            return Result.Success(new BulkResponse<Guid, Guid>([], 0, 0, 0));

        var alreadyAssigned = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => cohorts.Select(c => c.Id).Contains(a.CurrentCohortId))
            .Select(a => new { a.RegistrationId, a.CurrentCohortId })
            .ToListAsync(cancellationToken);

        var alreadyAssignedSet = alreadyAssigned
            .Select(a => (a.RegistrationId, a.CurrentCohortId))
            .ToHashSet();

        var results = new List<BulkItemResult<Guid, Guid>>();

        foreach (var cohort in cohorts)
        {
            var registrations = await dbContext.Registrations
                .AsNoTracking()
                .Where(r =>
                    r.AcademicGroupId == cohort.AcademicGroupId &&
                    r.LevelId         == cohort.LevelId          &&
                    r.Status          != RegistrationStatus.Withdrawn)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            foreach (var registrationId in registrations)
            {
                if (alreadyAssignedSet.Contains((registrationId, cohort.Id))) continue;

                var assignmentId = Guid.NewGuid();
                var assignment = new InternshipAssignment
                {
                    Id              = assignmentId,
                    RegistrationId  = registrationId,
                    CurrentCohortId = cohort.Id,
                };

                assignment.MembershipHistory.Add(new CohortMembership
                {
                    Id                     = Guid.NewGuid(),
                    InternshipAssignmentId = assignmentId,
                    CohortId               = cohort.Id,
                    StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
                });

                dbContext.InternshipAssignments.Add(assignment);
                results.Add(new BulkItemResult<Guid, Guid>(registrationId, assignmentId, null));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<Guid, Guid>(
            results,
            results.Count,
            results.Count,
            0));
    }
}
