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
    /// <returns>
    /// The number of périodes created. ⚠ <b>A count, where this used to answer a bare
    /// <c>Result</c></b> — an audited act has to say <i>how much</i>, and « Publier » on a cohorte of
    /// forty and on one of four wrote the same entry. The strict publish refuses everything it cannot
    /// materialise, so the figure is never a partial success being passed off as one.
    /// </returns>
    public async Task<Result<int>> PublishCohortAsync(int cohortId, bool allowOverCapacity, CancellationToken ct)
    {
        bool cohortExists = await dbContext.Cohorts.AnyAsync(c => c.Id == cohortId, ct);
        if (!cohortExists)
            return Result.Failure<int>(StageErrors.CohortNotFound(cohortId));

        if (await PublishedAssignmentsQuery(dbContext, cohortId).AnyAsync(ct))
            return Result.Failure<int>(StageErrors.ScheduleAlreadyPublished);

        var slotAssignments = await LoadSlotAssignmentsAsync([cohortId], null, ct);
        if (slotAssignments.Count == 0)
            return Result.Failure<int>(StageErrors.ScheduleNotConfigured);

        // ⚠ An assignment that already holds a period has already been served — an imported
        // historical rotation, a délocalisation, a revalidation. Publishing over it would add a
        // second set of periods for the same stage, which the score then averages and the lifecycle
        // then waits on. Publication materialises a plan; it never re-materialises a past.
        var assignmentIds = await UnservedAssignmentIdsQuery(dbContext, cohortId).ToListAsync(ct);

        if (assignmentIds.Count == 0)
            return Result.Failure<int>(StageErrors.NoPlannedAssignments);

        var intake = await EnsureIntakeAsync(slotAssignments, allowOverCapacity, ct);
        if (intake.IsFailure)
            return Result.Failure<int>(intake.Error);

        var periods = BuildPeriods(slotAssignments, assignmentIds);
        await dbContext.ServicePeriods.AddRangeAsync(periods, ct);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success(periods.Count);
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
    /// ⚠ <b>Three rules, and <paramref name="allowOverCapacity"/> is a request rather than a
    /// decision.</b>
    /// <list type="bullet">
    /// <item><b>Admissibility</b> — the service carries intake rules and none of them name this
    /// promotion. Checked whatever the caller asks for, and never waived: publishing anyway sends
    /// students to a service that does not take them, which no checkbox makes true.</item>
    /// <item><b>Occupancy on a service that allows the override</b> — over the number, on a service
    /// that has not refused. Waivable, because the number is a target and this base is structurally
    /// over-subscribed: measured 2026-08-14, <b>233 of 353 planned cells are over capacity (66%),
    /// worst 85 against 20</b>, and not one of the 148 services has an authored quota.</item>
    /// <item><b>Occupancy on a service that refuses it</b> —
    /// <see cref="Domain.Hospitals.Service.AllowsOverCapacity"/> is false. Same arithmetic, hard
    /// verdict: the chef has said his number is not a target, so the checkbox does not reach it and
    /// the refusal says so.</item>
    /// </list>
    /// <para>The third exists because of what the second measured: with two thirds of the plan over
    /// capacity the checkbox is ticked as a matter of routine, so a service's ceiling was in practice
    /// advisory for everybody. Making it firm is a decision <i>per service</i>, taken by the people
    /// the number is about, rather than a stricter default nobody could work under.</para>
    /// <para>⚠ <b>The override no longer means the occupancy half can be skipped.</b> It used to, and
    /// that was the cheap path the split of 2026-08-14 preserved; now the loads of the firm services
    /// still have to be counted. The lookup is built over <i>exactly</i> those, so a publish touching
    /// only permissive services measures nothing, as before.</para>
    /// </remarks>
    private async Task<Result> EnsureIntakeAsync(
        IReadOnlyCollection<SlotAssignmentInfo> slotAssignments, bool allowOverCapacity, CancellationToken ct)
    {
        if (slotAssignments.Count == 0)
            return Result.Success();

        var serviceIds = slotAssignments.Select(s => s.ServiceId).Distinct().ToList();
        var intake = await intakeCalculator.BuildAsync(serviceIds, ct);

        // Admissibility is answered by the intake rules alone; only a capacity verdict needs to know
        // how many students are actually there. With the override on, that leaves exactly the
        // services which refuse it — usually none, in which case the expensive half is never built.
        var measured = allowOverCapacity ? intake.FirmServicesAmong(serviceIds) : serviceIds;

        var occupancy = measured.Count > 0
            ? await occupancyCalculator.BuildAsync(measured, ct)
            : null;

        // ⚠ Every breach, not the first one. This runs over a whole stage — a hundred cohorts and
        // ten columns — and the base is structurally over-subscribed, so refusing on the first cell
        // meant the admin fixed one service, waited for the whole publish again, and was told about
        // the next. Worse, the screen that published cohort by cohort turned that into one refusal
        // toast per cohorte, dozens of them, none of which was the whole story. One pass, one
        // refusal, and it names how many cells are involved and the heaviest of them.
        var breaches = new List<IntakeBreach>();

        foreach (var sa in slotAssignments
                     .GroupBy(s => new { s.ServiceId, s.LevelId, s.StartDate, s.EndDate })
                     .Select(g => g.First()))
        {
            // What the caller asked for, met with what this service allows. Asked per cell because
            // one publish spans many services and they do not answer alike.
            bool forceable = intake.AllowsOverCapacity(sa.ServiceId);
            bool waived = allowOverCapacity && forceable;

            if (intake.HasLevelRestrictions(sa.ServiceId))
            {
                // Checked whatever the caller asked for. This is the hard half.
                if (!intake.Admits(sa.ServiceId, sa.LevelId))
                {
                    breaches.Add(IntakeBreach.NotAdmitted(sa));
                    continue;
                }

                if (waived) continue;

                // This promotion's students only: the quota is about them, and another promotion
                // filling its own quota is not this one's problem.
                int levelLoad = occupancy!.LoadOn(sa.ServiceId, sa.LevelId, sa.StartDate, sa.EndDate);
                int levelCapacity = intake.CapacityFor(sa.ServiceId, sa.LevelId);
                if (levelLoad > levelCapacity)
                    breaches.Add(IntakeBreach.OverLevelQuota(sa, levelLoad, levelCapacity, forceable));

                continue;
            }

            // An unrestricted service admits every promotion by definition, so there is no
            // admissibility half here — only the number, and whether this service lets it be forced.
            if (waived) continue;

            // One number for everybody, so the load is everybody. Blaming a "quota" here would send
            // the user looking for a rule nobody authored.
            int load = occupancy!.LoadOn(sa.ServiceId, sa.StartDate, sa.EndDate);
            int capacity = intake.TotalCapacity(sa.ServiceId);
            if (load > capacity)
                breaches.Add(IntakeBreach.OverCapacity(sa, load, capacity, forceable));
        }

        if (breaches.Count == 0)
            return Result.Success();

        // One cell in trouble is the per-cohorte case, and its own sentence already says everything
        // there is to say about it — including, for an inadmissible promotion or a service that
        // refuses the override, that no checkbox lifts it. Keep it: an aggregate wrapper around a
        // single breach reads as evasion.
        if (breaches.Count == 1)
            return Result.Failure(breaches[0].AsError());

        // Unforceable first: those are what the reader has to act on, since the checkbox will not
        // move them. Admissibility leads within that half — it is a fact about the service rather
        // than a matter of degree — and the rest is ordered by how far over each cell is.
        var ordered = breaches
            .OrderBy(b => b.Forceable)
            .ThenByDescending(b => b.IsAdmissibility)
            .ThenByDescending(b => b.Overflow)
            .ToList();

        return Result.Failure(StageErrors.PublishRefusedByIntake(
            ordered.Count,
            ordered.Count(b => b.IsAdmissibility),
            ordered.Count(b => !b.IsAdmissibility && !b.Forceable),
            ordered.Take(MaxReportedBreaches).Select(b => b.Summary).ToList()));
    }

    /// <summary>
    /// How many refused cells a refusal names one by one. The count is always exact; the list is
    /// what a person can read in a toast.
    /// </summary>
    private const int MaxReportedBreaches = 3;

    /// <summary>Which of the three rules a cell fell foul of.</summary>
    private enum BreachKind
    {
        /// <summary>The service carries intake rules and none names this promotion.</summary>
        NotAdmitted,

        /// <summary>Over the service's total, counted across every promotion sharing it.</summary>
        OverTotal,

        /// <summary>Over the quota this service grants the stage's promotion.</summary>
        OverQuota,
    }

    /// <summary>One cell a publish will not write, and why.</summary>
    /// <param name="Forceable">
    /// Whether « autoriser le dépassement d'effectif » would lift <i>this</i> cell — false for an
    /// inadmissible promotion, and false on a service whose chef has refused the override. It is what
    /// decides both the sentence and the order, because a refusal the checkbox cannot move is the one
    /// the reader has to act on.
    /// </param>
    private sealed record IntakeBreach(
        SlotAssignmentInfo Cell, BreachKind Kind, int Load, int Capacity, bool Forceable)
    {
        public static IntakeBreach NotAdmitted(SlotAssignmentInfo cell) =>
            new(cell, BreachKind.NotAdmitted, 0, 0, Forceable: false);

        public static IntakeBreach OverLevelQuota(
            SlotAssignmentInfo cell, int load, int capacity, bool forceable) =>
            new(cell, BreachKind.OverQuota, load, capacity, forceable);

        public static IntakeBreach OverCapacity(
            SlotAssignmentInfo cell, int load, int capacity, bool forceable) =>
            new(cell, BreachKind.OverTotal, load, capacity, forceable);

        public bool IsAdmissibility => Kind == BreachKind.NotAdmitted;

        /// <summary>How far over the governing limit the cell is — 0 for an admissibility refusal,
        /// which is not a matter of degree.</summary>
        public int Overflow => IsAdmissibility ? 0 : Load - Capacity;

        public string Summary => IsAdmissibility
            ? $"P{Cell.PeriodNumber} « {Cell.ServiceName} » : {Cell.LevelLabel} non admise"
            : $"P{Cell.PeriodNumber} « {Cell.ServiceName} » : {Load}/{Capacity}"
              + (Forceable ? "" : " (dépassement refusé)");

        public Error AsError() => Kind switch
        {
            BreachKind.NotAdmitted => StageErrors.LevelNotAdmitted(
                Cell.PeriodNumber, Cell.ServiceName, Cell.LevelLabel, Cell.StartDate, Cell.EndDate),

            // Its own sentence, not a variant of the two below: what has to be said first is that the
            // checkbox on screen will not lift this one, and a message ending on « cochez… » would
            // send the admin round a loop the service has already closed.
            _ when !Forceable => StageErrors.OverCapacityRefusedByService(
                Cell.PeriodNumber, Cell.ServiceName,
                Kind == BreachKind.OverQuota ? Cell.LevelLabel : null,
                Cell.StartDate, Cell.EndDate, Load, Capacity),

            BreachKind.OverQuota => StageErrors.LevelCapacityExceeded(
                Cell.PeriodNumber, Cell.ServiceName, Cell.LevelLabel,
                Cell.StartDate, Cell.EndDate, Load, Capacity),

            _ => StageErrors.CapacityExceeded(
                Cell.PeriodNumber, Cell.ServiceName, Cell.StartDate, Cell.EndDate, Load, Capacity),
        };
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
                    CohortSlotAssignmentId = stay.LeadCellId,
                    StartDate              = stay.StartDate,
                    EndDate                = stay.EndDate,
                    IsComplete             = false,
                };

                foreach (int cellId in stay.CellIds)
                    period.SlotCoverage.Add(new ServicePeriodSlotCoverage
                    {
                        CohortSlotAssignmentId = cellId,
                    });

                periods.Add(period);
            }

        return periods;
    }

    /// <summary>
    /// Groups every cohorte's cells into the stays they represent, cohorte by cohorte.
    /// </summary>
    /// <remarks>
    /// The rule itself is <see cref="CohortStayFolder"/>'s and lives in the domain, because a second
    /// act now has to produce the périodes a cohorte's member holds — « changement de groupe ». Kept
    /// private here is what would have made a student's <see cref="StageRotationMode.SingleService"/>
    /// run fold one way when it was published and another way when he was moved into it.
    /// </remarks>
    private static List<CohortStay> BuildStays(IReadOnlyCollection<SlotAssignmentInfo> slotAssignments)
    {
        var stays = new List<CohortStay>();

        foreach (var group in slotAssignments.GroupBy(sa => sa.CohortId))
        {
            var cells = group
                .Select(sa => new CohortCell(sa.Id, sa.PeriodNumber, sa.ServiceId, sa.StartDate, sa.EndDate))
                .ToList();

            stays.AddRange(CohortStayFolder.Fold(cells, group.First().RotationMode));
        }

        return stays;
    }

    /// <summary>One student assignment of a cohort, and whether it already holds a période.</summary>
    internal sealed record CandidateAssignment(Guid Id, int CohortId, bool AlreadyServed);

    internal sealed record SlotAssignmentInfo(
        int Id, int CohortId, int ServiceId, DateOnly StartDate, DateOnly EndDate,
        int PeriodNumber, string ServiceName, int LevelId, string LevelLabel,
        StageRotationMode RotationMode);
}
