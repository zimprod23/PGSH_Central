using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

/// <param name="SkippedAlreadyServed">
/// Student assignments that already carried a service period and were therefore left alone. These
/// are the stages someone has already done — an imported historical rotation, a délocalisation, a
/// revalidation — and publishing over them would duplicate the stage rather than schedule it.
/// </param>
public sealed record PublishResult(
    int PublishedCohorts, int PeriodsCreated, int SkippedCohorts, int SkippedAlreadyServed = 0);

/// <summary>
/// Materialises the planned schedule into execution records: one
/// <see cref="ServicePeriod"/> per (student assignment × slot assignment).
/// A cohort counts as published once any of its assignments has a service
/// period linked to a slot assignment. Supports the strict per-cohort publish
/// (returns business failures) and a lenient per-stage publish that skips
/// already-published or unconfigured cohorts, optionally scoped to a partition
/// and/or a window of periods.
/// </summary>
internal sealed class SchedulePublisher(
    IApplicationDbContext dbContext,
    ServiceOccupancyCalculator occupancyCalculator,
    ServiceIntakeCalculator intakeCalculator)
{
    public async Task<Result> PublishCohortAsync(int cohortId, bool allowOverCapacity, CancellationToken ct)
    {
        bool cohortExists = await dbContext.Cohorts.AnyAsync(c => c.Id == cohortId, ct);
        if (!cohortExists)
            return Result.Failure(StageErrors.CohortNotFound(cohortId));

        if (await PublishedAssignmentsQuery(dbContext, cohortId).AnyAsync(ct))
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        var slotAssignments = await LoadSlotAssignmentsAsync([cohortId], null, ct);
        if (slotAssignments.Count == 0)
            return Result.Failure(StageErrors.ScheduleNotConfigured);

        // ⚠ An assignment that already holds a period has already been served — an imported
        // historical rotation, a délocalisation, a revalidation. Publishing over it would add a
        // second set of periods for the same stage, which the score then averages and the lifecycle
        // then waits on. Publication materialises a plan; it never re-materialises a past.
        var assignmentIds = await UnservedAssignmentIdsQuery(dbContext, cohortId).ToListAsync(ct);

        if (assignmentIds.Count == 0)
            return Result.Failure(StageErrors.NoPlannedAssignments);

        var intake = await EnsureIntakeAsync(slotAssignments, allowOverCapacity, ct);
        if (intake.IsFailure)
            return intake;

        var periods = BuildPeriods(slotAssignments, assignmentIds);
        await dbContext.ServicePeriods.AddRangeAsync(periods, ct);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PublishResult>> PublishStageAsync(
        int stageId,
        int academicYearId,
        IReadOnlyCollection<string>? partitionLabels,
        IReadOnlyCollection<int>? periodNumbers,
        bool allowOverCapacity,
        CancellationToken ct)
    {
        var cohortIds = await CohortIdsQuery(dbContext, stageId, academicYearId, partitionLabels)
            .ToListAsync(ct);
        if (cohortIds.Count == 0)
            return Result.Success(new PublishResult(0, 0, 0));

        var publishedCohortIds =
            (await PublishedCohortIdsQuery(dbContext, cohortIds).ToListAsync(ct)).ToHashSet();

        // Already-served assignments are excluded, not skipped as whole cohorts: a cohort routinely
        // mixes students who have the stage behind them (repeaters, délocalisés) with students who
        // do not, and the latter still need their schedule. See PublishCohortAsync for why.
        var candidates = await CandidateAssignmentsQuery(dbContext, cohortIds).ToListAsync(ct);

        int skippedAlreadyServed = candidates.Count(a => a.AlreadyServed);

        var assignmentsByCohort = candidates
            .Where(a => !a.AlreadyServed)
            .GroupBy(a => a.CohortId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        var slotAssignmentsByCohort = (await LoadSlotAssignmentsAsync(cohortIds, periodNumbers, ct))
            .GroupBy(sa => sa.CohortId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var newPeriods = new List<ServicePeriod>();
        var publishableSlots = new List<SlotAssignmentInfo>();
        int published = 0, skipped = 0;

        foreach (var cohortId in cohortIds)
        {
            if (publishedCohortIds.Contains(cohortId)
                || !slotAssignmentsByCohort.TryGetValue(cohortId, out var slots) || slots.Count == 0
                || !assignmentsByCohort.TryGetValue(cohortId, out var assignmentIds) || assignmentIds.Count == 0)
            {
                skipped++;
                continue;
            }

            publishableSlots.AddRange(slots);
            newPeriods.AddRange(BuildPeriods(slots, assignmentIds));
            published++;
        }

        var intake = await EnsureIntakeAsync(publishableSlots, allowOverCapacity, ct);
        if (intake.IsFailure)
            return Result.Failure<PublishResult>(intake.Error);

        if (newPeriods.Count > 0)
        {
            await dbContext.ServicePeriods.AddRangeAsync(newPeriods, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return Result.Success(new PublishResult(published, newPeriods.Count, skipped, skippedAlreadyServed));
    }

    private Task<List<SlotAssignmentInfo>> LoadSlotAssignmentsAsync(
        IReadOnlyCollection<int> cohortIds, IReadOnlyCollection<int>? periodNumbers, CancellationToken ct) =>
        SlotAssignmentsQuery(dbContext, cohortIds, periodNumbers).ToListAsync(ct);

    /// <summary>The cohorts a stage-wide publish is being asked to cover.</summary>
    /// <remarks>
    /// ⚠ Every query on this class is named so <c>SqlTranslationTests</c> can compile it against the
    /// Npgsql provider. <b>Nothing here has ever run against PostgreSQL</b>: the Med6 rehearsal of
    /// 2026-08-26 was <c>publish: false</c>, and the base holds 0 grid-linked périodes, so the first
    /// real publication would be the first execution. A translation failure surfaces there — the act
    /// with the least appetite in the system for a 500.
    /// </remarks>
    internal static IQueryable<int> CohortIdsQuery(
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

        return query.Select(c => c.Id);
    }

    /// <summary>
    /// The assignments of one cohort that already hold a period which came from the grid — i.e. the
    /// evidence that this cohort's schedule has been published.
    /// </summary>
    /// <remarks>
    /// ⚠ The caller wraps this in <c>AnyAsync</c>, so the SQL it runs is an <c>EXISTS</c> rather than
    /// the <c>SELECT</c> the test compiles. That is not a hole: what fails to translate is the
    /// <em>predicate</em> — measured 2026-08-26, a client-side call in a projection is evaluated on
    /// the client and compiles fine, while the same call in a <c>Where</c> throws — and the predicate
    /// is identical either way.
    /// </remarks>
    internal static IQueryable<InternshipAssignment> PublishedAssignmentsQuery(
        IApplicationDbContext dbContext, int cohortId) =>
        dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == cohortId
                     && a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null));

    /// <summary>
    /// The assignments of one cohort that nobody has served yet — the only ones a publish may
    /// materialise. See <see cref="PublishCohortAsync"/> for why an already-served one is left alone.
    /// </summary>
    internal static IQueryable<Guid> UnservedAssignmentIdsQuery(
        IApplicationDbContext dbContext, int cohortId) =>
        dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == cohortId && !a.ServicePeriods.Any())
            .Select(a => a.Id);

    /// <summary>
    /// Which cohorts already hold a period that came from the grid — the ones a stage-wide publish
    /// must leave alone.
    /// </summary>
    internal static IQueryable<int> PublishedCohortIdsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId)
                     && a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null))
            .Select(a => a.CurrentCohortId)
            .Distinct();

    /// <summary>
    /// Every student assignment of these cohorts, each carrying whether it has already been served.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>AlreadyServed</c> is a correlated <c>Any()</c> inside the projection, which is the family
    /// the <c>CohortProvisioner</c> defect came from — an <c>EXISTS</c> subquery translates where a
    /// collection of computed elements does not, and only compiling it proves which side of that line
    /// it falls on. Named for <c>SqlTranslationTests</c>.
    /// </remarks>
    internal static IQueryable<CandidateAssignment> CandidateAssignmentsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.InternshipAssignments
            .Where(a => cohortIds.Contains(a.CurrentCohortId))
            .Select(a => new CandidateAssignment(a.Id, a.CurrentCohortId, a.ServicePeriods.Any()));

    /// <summary>
    /// The planning cells of these cohorts, with everything publication needs to shape a period out
    /// of them: the window, the service, and the level the intake rules are read against.
    /// </summary>
    /// <remarks>
    /// ⚠ The heaviest projection on the publish path — four navigation hops, a null-coalesce over a
    /// concatenation (<c>"niveau " + LevelId</c>) and an enum with a string conversion. Named for
    /// <c>SqlTranslationTests</c>.
    /// </remarks>
    internal static IQueryable<SlotAssignmentInfo> SlotAssignmentsQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> cohortIds,
        IReadOnlyCollection<int>? periodNumbers)
    {
        var query = dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CohortId));

        if (periodNumbers is { Count: > 0 })
            query = query.Where(a => periodNumbers.Contains(a.StageSlot.PeriodNumber));

        return query
            .Select(a => new SlotAssignmentInfo(
                a.Id, a.CohortId, a.ServiceId, a.StageSlot.StartDate, a.StageSlot.EndDate,
                a.StageSlot.PeriodNumber, a.Service.Name,
                a.Cohort.Stage.LevelId,
                a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                a.Cohort.Stage.RotationMode));
    }

    /// <summary>
    /// Checks what a service will take, over every overlapping window — counted globally across every
    /// stage, so a service shared by two partitions running different stages on overlapping dates
    /// cannot be silently over-filled. The cohort being published is already part of the planned
    /// occupancy the lookup measures.
    ///
    /// <b>One</b> ceiling per service, not two — quotas replace the total rather than sitting under
    /// it. A restricted service is measured per promotion against that promotion's quota; an
    /// unrestricted one against its total, across every promotion at once. So a service of 20
    /// granting 10 and 15 publishes 6 + 15 = 21 without complaint, and the same service with no
    /// quotas refuses at 21. See <see cref="Domain.Hospitals.Service.CapacityFor"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Two rules of different kinds, and <paramref name="allowOverCapacity"/> waives only one.</b>
    /// <list type="bullet">
    /// <item><b>Admissibility</b> — the service carries intake rules and none of them name this
    /// promotion. Not negotiable, and not waivable: publishing anyway sends students to a service that
    /// does not take them, which no checkbox makes true.</item>
    /// <item><b>Occupancy</b> — the service takes this promotion but would hold more than its number.
    /// Negotiable, because the number is a target and this base is structurally over-subscribed:
    /// measured 2026-08-14, <b>233 of 353 planned cells are over capacity (66%), worst 85 against
    /// 20</b>, and not one of the 148 services has an authored quota — every capacity verdict today is
    /// measured against the imported default of 20.</item>
    /// </list>
    /// One flag governing both is what made this wrong: with two thirds of the plan over capacity the
    /// checkbox is ticked as a matter of routine, and it was silently switching off the hard rule
    /// alongside the soft one. A rule that is only enforced when nobody needs the override is not
    /// enforced.
    /// </remarks>
    private async Task<Result> EnsureIntakeAsync(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, bool allowOverCapacity, CancellationToken ct)
    {
        if (slotAssignments.Count == 0)
            return Result.Success();

        var serviceIds = slotAssignments.Select(s => s.ServiceId).Distinct().ToList();
        var intake = await intakeCalculator.BuildAsync(serviceIds, ct);

        // Admissibility is answered by the intake rules alone; only a capacity verdict needs to know
        // how many students are actually there. So with the override on, the expensive half is never
        // built — which is what keeps splitting the flag from making the common publish slower than
        // it was when the flag skipped everything.
        var occupancy = allowOverCapacity
            ? null
            : await occupancyCalculator.BuildAsync(serviceIds, ct);

        foreach (var sa in slotAssignments
                     .GroupBy(s => new { s.ServiceId, s.LevelId, s.StartDate, s.EndDate })
                     .Select(g => g.First()))
        {
            if (intake.HasLevelRestrictions(sa.ServiceId))
            {
                // Checked whatever the caller asked for. This is the hard half.
                if (!intake.Admits(sa.ServiceId, sa.LevelId))
                    return Result.Failure(StageErrors.LevelNotAdmitted(
                        sa.PeriodNumber, sa.ServiceName, sa.LevelLabel, sa.StartDate, sa.EndDate));

                if (allowOverCapacity) continue;

                // This promotion's students only: the quota is about them, and another promotion
                // filling its own quota is not this one's problem.
                int levelLoad = occupancy!.LoadOn(sa.ServiceId, sa.LevelId, sa.StartDate, sa.EndDate);
                int levelCapacity = intake.CapacityFor(sa.ServiceId, sa.LevelId);
                if (levelLoad > levelCapacity)
                    return Result.Failure(StageErrors.LevelCapacityExceeded(
                        sa.PeriodNumber, sa.ServiceName, sa.LevelLabel,
                        sa.StartDate, sa.EndDate, levelLoad, levelCapacity));

                continue;
            }

            // An unrestricted service admits every promotion by definition, so there is no hard half
            // here — only the number, and the number is what the override is for.
            if (allowOverCapacity) continue;

            // One number for everybody, so the load is everybody. Blaming a "quota" here would send
            // the user looking for a rule nobody authored.
            int load = occupancy!.LoadOn(sa.ServiceId, sa.StartDate, sa.EndDate);
            int capacity = intake.TotalCapacity(sa.ServiceId);
            if (load > capacity)
                return Result.Failure(StageErrors.CapacityExceeded(
                    sa.PeriodNumber, sa.ServiceName, sa.StartDate, sa.EndDate, load, capacity));
        }

        return Result.Success();
    }

    /// <summary>
    /// One <see cref="ServicePeriod"/> per student per <i>stay</i>. Under
    /// <see cref="StageRotationMode.PerPeriod"/> a stay is a single cell; under
    /// <see cref="StageRotationMode.SingleService"/> it is the whole run the group spends in one
    /// service, so the run's cells collapse into one continuous period carrying one evaluation.
    /// </summary>
    private static List<ServicePeriod> BuildPeriods(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, IReadOnlyCollection<Guid> assignmentIds)
    {
        var stays = BuildStays(slotAssignments);
        var periods = new List<ServicePeriod>(stays.Count * assignmentIds.Count);

        foreach (var stay in stays)
            foreach (var assignmentId in assignmentIds)
            {
                // Do NOT pre-set the coverage rows' Id: they are children of a brand-new period and
                // EF generates the keys (see InternshipAssignment.Delocalize for the failure mode).
                var period = new ServicePeriod
                {
                    InternshipAssignmentId = assignmentId,
                    ServiceId              = stay.ServiceId,
                    CohortSlotAssignmentId = stay.Cells[0].Id,
                    StartDate              = stay.StartDate,
                    EndDate                = stay.EndDate,
                    IsComplete             = false,
                };

                foreach (var cell in stay.Cells)
                    period.SlotCoverage.Add(new ServicePeriodSlotCoverage
                    {
                        CohortSlotAssignmentId = cell.Id,
                    });

                periods.Add(period);
            }

        return periods;
    }

    /// <summary>
    /// Groups a cohort's cells into the stays they represent.
    ///
    /// <para>A run is a maximal set of that cohort's cells with <b>consecutive period numbers and the
    /// same service</b>. Deriving it from the cells rather than from the caller's window is what makes
    /// it general: publishing one concurrency block and publishing the whole stage both produce the
    /// same stays, because each cohort only ever holds the cells of its own run. Breaking on a service
    /// change matters too — a cell edited by hand to a different service is two stays, not one period
    /// silently spanning both.</para>
    /// </summary>
    private static List<Stay> BuildStays(IReadOnlyCollection<SlotAssignmentInfo> slotAssignments)
    {
        var stays = new List<Stay>();

        foreach (var group in slotAssignments.GroupBy(sa => sa.CohortId))
        {
            var ordered = group.OrderBy(sa => sa.PeriodNumber).ToList();

            if (ordered[0].RotationMode != StageRotationMode.SingleService)
            {
                stays.AddRange(ordered.Select(sa => new Stay([sa], sa.ServiceId, sa.StartDate, sa.EndDate)));
                continue;
            }

            var run = new List<SlotAssignmentInfo> { ordered[0] };
            for (int i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var current  = ordered[i];

                if (current.PeriodNumber == previous.PeriodNumber + 1 && current.ServiceId == previous.ServiceId)
                {
                    run.Add(current);
                    continue;
                }

                stays.Add(Close(run));
                run = [current];
            }
            stays.Add(Close(run));
        }

        return stays;

        static Stay Close(List<SlotAssignmentInfo> run) => new(
            [.. run], run[0].ServiceId, run.Min(c => c.StartDate), run.Max(c => c.EndDate));
    }

    private sealed record Stay(
        IReadOnlyList<SlotAssignmentInfo> Cells, int ServiceId, DateOnly StartDate, DateOnly EndDate);

    /// <summary>One student assignment of a cohort, and whether it already holds a période.</summary>
    internal sealed record CandidateAssignment(Guid Id, int CohortId, bool AlreadyServed);

    internal sealed record SlotAssignmentInfo(
        int Id, int CohortId, int ServiceId, DateOnly StartDate, DateOnly EndDate,
        int PeriodNumber, string ServiceName, int LevelId, string LevelLabel,
        StageRotationMode RotationMode);
}
