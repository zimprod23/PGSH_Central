using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Assign;

internal sealed class AssignStudentsToCohortCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignStudentsToCohortCommand, BulkResponse<Guid, Guid>>
{
    public async Task<Result<BulkResponse<Guid, Guid>>> Handle(
        AssignStudentsToCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == request.CohortId)
            .Select(c => new
            {
                c.Id,
                c.AcademicGroupId,
                LevelId = c.Stage.LevelId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (cohort is null)
            return Result.Failure<BulkResponse<Guid, Guid>>(Error.NotFound(
                "Cohorts.NotFound",
                $"The cohort with Id = '{request.CohortId}' was not found."));

        // All registrations in this group at the correct level, not already assigned to this cohort
        var alreadyAssigned = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.CurrentCohortId == request.CohortId)
            .Select(a => a.RegistrationId)
            .ToHashSetAsync(cancellationToken);

        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r =>
                r.AcademicGroupId == cohort.AcademicGroupId &&
                r.LevelId         == cohort.LevelId          &&
                r.Status          != RegistrationStatus.Withdrawn &&
                !alreadyAssigned.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (registrations.Count == 0)
            return Result.Success(new BulkResponse<Guid, Guid>([], 0, 0, 0));

        var results = new List<BulkItemResult<Guid, Guid>>(registrations.Count);

        foreach (var registrationId in registrations)
        {
            var assignmentId = Guid.NewGuid();
            var assignment = new InternshipAssignment
            {
                Id             = assignmentId,
                RegistrationId = registrationId,
                CurrentCohortId = request.CohortId,
            };

            assignment.MembershipHistory.Add(new CohortMembership
            {
                Id                     = Guid.NewGuid(),
                InternshipAssignmentId = assignmentId,
                CohortId               = request.CohortId,
                StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
            });

            dbContext.InternshipAssignments.Add(assignment);
            results.Add(new BulkItemResult<Guid, Guid>(registrationId, assignmentId, null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<Guid, Guid>(
            results,
            registrations.Count,
            results.Count,
            0));
    }
}
