using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Creates <see cref="InternshipAssignment"/> records (with opening
/// <see cref="CohortMembership"/>) for the eligible students of one or more
/// cohorts. A student is eligible when their registration sits in the cohort's
/// academic group at the cohort's level and is not withdrawn, and they are not
/// already assigned to that cohort. Shared by per-cohort affectation, per-stage
/// affectation (optionally partition-scoped), and the macro-plan orchestrator.
/// </summary>
internal sealed class StudentAffectationService(IApplicationDbContext dbContext)
{
    private sealed record CohortTarget(int CohortId, int AcademicGroupId, int LevelId);

    public async Task<Result<BulkResponse<Guid, Guid>>> AssignToCohortAsync(int cohortId, CancellationToken ct)
    {
        var cohort = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => new CohortTarget(c.Id, c.AcademicGroupId, c.Stage.LevelId))
            .FirstOrDefaultAsync(ct);

        if (cohort is null)
            return Result.Failure<BulkResponse<Guid, Guid>>(Error.NotFound(
                "Cohorts.NotFound", $"The cohort with Id = '{cohortId}' was not found."));

        return Result.Success(await AssignAsync([cohort], ct));
    }

    public async Task<BulkResponse<Guid, Guid>> AssignByStageAsync(
        int stageId, IReadOnlyCollection<string>? partitionLabels, CancellationToken ct,
        int? academicYearId = null)
    {
        var query = dbContext.Cohorts.AsNoTracking().Where(c => c.StageId == stageId);

        if (academicYearId is not null)
            query = query.Where(c => c.AcademicGroup.AcademicYearId == academicYearId);

        if (partitionLabels is { Count: > 0 })
            query = query.Where(c => c.AcademicGroup.RotationGroup != null
                                  && partitionLabels.Contains(c.AcademicGroup.RotationGroup));

        var cohorts = await query
            .Select(c => new CohortTarget(c.Id, c.AcademicGroupId, c.Stage.LevelId))
            .ToListAsync(ct);

        return await AssignAsync(cohorts, ct);
    }

    private async Task<BulkResponse<Guid, Guid>> AssignAsync(IReadOnlyList<CohortTarget> cohorts, CancellationToken ct)
    {
        if (cohorts.Count == 0)
            return new BulkResponse<Guid, Guid>([], 0, 0, 0);

        var cohortIds = cohorts.Select(c => c.CohortId).ToList();

        var alreadyAssigned = (await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => new { a.RegistrationId, a.CurrentCohortId })
            .ToListAsync(ct))
            .Select(a => (a.RegistrationId, a.CurrentCohortId))
            .ToHashSet();

        int totalEligible = 0;
        var results = new List<BulkItemResult<Guid, Guid>>();

        foreach (var cohort in cohorts)
        {
            var registrations = await dbContext.Registrations
                .AsNoTracking()
                .Where(r => r.AcademicGroupId == cohort.AcademicGroupId
                         && r.LevelId == cohort.LevelId
                         && r.Status != RegistrationStatus.Withdrawn)
                .Select(r => r.Id)
                .ToListAsync(ct);

            foreach (var registrationId in registrations)
            {
                totalEligible++;
                if (!alreadyAssigned.Add((registrationId, cohort.CohortId))) continue;

                var assignmentId = Guid.NewGuid();
                var assignment = new InternshipAssignment
                {
                    Id              = assignmentId,
                    RegistrationId  = registrationId,
                    CurrentCohortId = cohort.CohortId,
                };

                assignment.MembershipHistory.Add(new CohortMembership
                {
                    Id                     = Guid.NewGuid(),
                    InternshipAssignmentId = assignmentId,
                    CohortId               = cohort.CohortId,
                    StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
                });

                dbContext.InternshipAssignments.Add(assignment);
                results.Add(new BulkItemResult<Guid, Guid>(registrationId, assignmentId, null));
            }
        }

        if (results.Count > 0)
            await dbContext.SaveChangesAsync(ct);

        return new BulkResponse<Guid, Guid>(results, totalEligible, results.Count, 0);
    }
}
