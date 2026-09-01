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
    internal sealed record CohortTarget(int CohortId, int AcademicGroupId, int LevelId);

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

    /// <summary>
    /// Affects every student of the stage's cohorts for one academic year. The year is required:
    /// a stage keeps a cohort per (group, year), so an unscoped run reached every promotion that
    /// ever took it.
    /// </summary>
    public async Task<BulkResponse<Guid, Guid>> AssignByStageAsync(
        int stageId, int academicYearId, IReadOnlyCollection<string>? partitionLabels, CancellationToken ct)
    {
        var cohorts = await StageCohortsQuery(dbContext, stageId, academicYearId, partitionLabels)
            .ToListAsync(ct);

        return await AssignAsync(cohorts, ct);
    }

    /// <summary>
    /// Affects <em>one</em> registration to every cohort its new roster already holds, for the stages of
    /// its own level. The bulk paths above walk cohorts and pick up their students; this walks one
    /// student and picks up his cohorts — the direction a late arrival needs, since nobody is going to
    /// re-run the affectation of the whole promotion for him.
    /// </summary>
    /// <remarks>
    /// ⚠ Does <b>not</b> save. The caller is joining a roster, which is several writes that must land
    /// together — the group pointer, these assignments and the periods materialised from them.
    /// </remarks>
    public async Task<IReadOnlyList<InternshipAssignment>> AssignRegistrationAsync(
        Registration registration, int academicGroupId, CancellationToken ct)
    {
        var cohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroupId == academicGroupId && c.Stage.LevelId == registration.LevelId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (cohorts.Count == 0)
            return [];

        var alreadyAssigned = (await dbContext.InternshipAssignments
                .AsNoTracking()
                .Where(a => a.RegistrationId == registration.Id && cohorts.Contains(a.CurrentCohortId))
                .Select(a => a.CurrentCohortId)
                .ToListAsync(ct))
            .ToHashSet();

        var created = new List<InternshipAssignment>();

        foreach (int cohortId in cohorts)
        {
            if (alreadyAssigned.Contains(cohortId)) continue;

            var assignmentId = Guid.NewGuid();
            var assignment = new InternshipAssignment
            {
                Id              = assignmentId,
                RegistrationId  = registration.Id,
                CurrentCohortId = cohortId,
            };

            assignment.MembershipHistory.Add(new CohortMembership
            {
                Id                     = Guid.NewGuid(),
                InternshipAssignmentId = assignmentId,
                CohortId               = cohortId,
                StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
            });

            dbContext.InternshipAssignments.Add(assignment);
            created.Add(assignment);
        }

        return created;
    }

    /// <summary>
    /// The stage's cohorts for one year, optionally narrowed to some partitions — each with the level
    /// whose registrations are the ones to affect.
    /// </summary>
    /// <remarks>
    /// Named so <c>SqlTranslationTests</c> can compile it. The shape is plain (navigation properties
    /// into a constructor projection), and it has run on the real base; the case exists because this
    /// is on the macro-plan path, and what makes a query untranslatable is a fact about the provider
    /// rather than about how complicated the C# looks.
    /// </remarks>
    internal static IQueryable<CohortTarget> StageCohortsQuery(
        IApplicationDbContext dbContext,
        int stageId,
        int academicYearId,
        IReadOnlyCollection<string>? partitionLabels)
    {
        var query = dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId);

        if (partitionLabels is { Count: > 0 })
            query = query.Where(c => c.AcademicGroup.RotationGroup != null
                                  && partitionLabels.Contains(c.AcademicGroup.RotationGroup));

        return query.Select(c => new CohortTarget(c.Id, c.AcademicGroupId, c.Stage.LevelId));
    }

    /// <summary>One registration eligible for affectation, with the pair it is eligible under.</summary>
    internal sealed record EligibleRegistration(Guid RegistrationId, int AcademicGroupId, int LevelId);

    /// <summary>
    /// Every registration standing in one of these rosters at one of these levels, not withdrawn —
    /// the candidates of every cohort of the call, read once.
    /// </summary>
    /// <remarks>
    /// ⚠ Named so <c>SqlTranslationTests</c> can compile it: it dereferences a nullable FK inside the
    /// projection (<c>r.AcademicGroupId!.Value</c>) after testing it in the predicate, and whether a
    /// provider accepts that is a fact about the provider.
    /// </remarks>
    internal static IQueryable<EligibleRegistration> EligibleRegistrationsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> academicGroupIds,
        IReadOnlyCollection<int> levelIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicGroupId != null
                     && academicGroupIds.Contains(r.AcademicGroupId.Value)
                     && levelIds.Contains(r.LevelId)
                     && r.Status != RegistrationStatus.Withdrawn)
            .Select(r => new EligibleRegistration(r.Id, r.AcademicGroupId!.Value, r.LevelId));

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

        // ⚠ One query for every cohort of the call, not one per cohort. This loop runs over the
        // cohorts of a whole promotion — 105 on the current year's biggest stage, and the macro plan
        // walks it once per concurrency block — so a per-cohort read was ~700 round trips for a
        // single « Générer le plan », which is where its minutes went.
        //
        // The pairing is preserved exactly: the rows are fetched for the groups and levels in play
        // and then keyed on **(group, level) together**, never on either alone. A registration whose
        // group is in the set and whose level is in the set is not necessarily the one this cohort
        // wants — that is the same trap as filtering by level and year with two independent Any's.
        var eligibleByGroupLevel = (await EligibleRegistrationsQuery(
                    dbContext,
                    cohorts.Select(c => c.AcademicGroupId).Distinct().ToList(),
                    cohorts.Select(c => c.LevelId).Distinct().ToList())
                .ToListAsync(ct))
            .GroupBy(r => (r.AcademicGroupId, r.LevelId))
            .ToDictionary(g => g.Key, g => g.Select(r => r.RegistrationId).ToList());

        int totalEligible = 0;
        var results = new List<BulkItemResult<Guid, Guid>>();

        foreach (var cohort in cohorts)
        {
            var registrations = eligibleByGroupLevel.GetValueOrDefault(
                (cohort.AcademicGroupId, cohort.LevelId), []);

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
