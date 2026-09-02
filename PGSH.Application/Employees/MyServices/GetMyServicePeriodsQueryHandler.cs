using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Application.Stages.ServicePeriods;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

internal sealed class GetMyServicePeriodsQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetMyServicePeriodsQuery, ChefWorklistResponse>
{
    public async Task<Result<ChefWorklistResponse>> Handle(
        GetMyServicePeriodsQuery request, CancellationToken cancellationToken)
    {
        var chefServiceIds = await authorizer.ChefServiceIdsAsync(cancellationToken);

        if (request.ServiceId.HasValue)
            chefServiceIds = chefServiceIds.Where(id => id == request.ServiceId.Value).ToList();

        if (chefServiceIds.Count == 0)
            return Empty(request);

        int? academicYearId = await ResolveYearAsync(request, cancellationToken);

        var counts = await CountAsync(
            chefServiceIds, academicYearId, request.SearchTerm, cancellationToken);

        int outsideYear = await OutsideYearCountAsync(
            chefServiceIds, request, academicYearId, counts, cancellationToken);

        var page = await LoadPageAsync(chefServiceIds, request, academicYearId, cancellationToken);

        // Students who transferred into the service but whose periods were never re-published have
        // no ServicePeriod row at all, so they cannot be paged with the rest. They are an overlay on
        // the live slice: bounded by the number of transfers (0 across the whole base on 2026-08-29),
        // shown on the first page of Current only, and counted into that slice's total so the pager
        // stays honest about how many rows the caller can reach.
        List<ServicePeriodResponse> incoming = request.EffectiveState == ServicePeriodState.Underway
            ? await LoadIncomingTransfersAsync(chefServiceIds, academicYearId, cancellationToken)
            : [];

        var items = page.PageNumber == 1
            ? incoming.Concat(page.Items).ToList()
            : page.Items;

        return new ChefWorklistResponse(
            new PaginatedResponse<ServicePeriodResponse>(
                items, page.PageNumber, page.PageSize, page.TotalCount + incoming.Count),
            request.EffectiveState,
            counts with { Underway = counts.Underway + incoming.Count },
            academicYearId,
            outsideYear);
    }

    private static Result<ChefWorklistResponse> Empty(GetMyServicePeriodsQuery request) =>
        new ChefWorklistResponse(
            new PaginatedResponse<ServicePeriodResponse>([], 1, request.EffectivePageSize, 0),
            request.EffectiveState,
            new ChefWorklistCounts(0, 0, 0, 0),
            AcademicYearId: null,
            OutsideYearCount: 0);

    // ─── The one predicate ────────────────────────────────────────────────────

    /// <summary>
    /// The periods of <paramref name="serviceIds"/> that are in <paramref name="state"/>. Both the
    /// page and the badge counts go through here, so a slice's stated size and its contents are the
    /// same question asked twice — they cannot drift.
    ///
    /// <para>The state predicate itself belongs to the domain
    /// (<see cref="ServicePeriodLifecycle"/>): what "en cours" means about a rotation is not a fact
    /// about this screen, and it was already being restated as a raw boolean triple in the planning
    /// services.</para>
    ///
    /// <para>Named and <c>internal static</c> on purpose: a query buried in a private async method
    /// cannot be handed to <c>ToQueryString()</c>, and the in-memory provider translates nothing.
    /// See <c>SqlTranslationTests</c>.</para>
    /// </summary>
    internal static IQueryable<ServicePeriod> ScopedQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> serviceIds,
        ServicePeriodState state,
        int? academicYearId = null,
        string? searchTerm = null)
    {
        var query = dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => serviceIds.Contains(p.ServiceId));

        // ⚠ Server-side, because the list is now a page. Searching the fetched rows only — which is
        // all a client can do — silently answers "not in this service" for a student who is simply
        // on page 3, and the chef has no way to tell the two apart. Lower-cased on both sides for
        // every field in the predicate: one field left un-lowered is a search that works for some
        // students and not others.
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            string term = searchTerm.Trim().ToLower();
            query = query.Where(p =>
                (p.InternshipAssignment.Registration.Student.FirstName ?? "").ToLower().Contains(term) ||
                (p.InternshipAssignment.Registration.Student.LastName ?? "").ToLower().Contains(term) ||
                (p.InternshipAssignment.Registration.Student.CNE ?? "").ToLower().Contains(term) ||
                p.InternshipAssignment.Registration.Student.Appogee.ToLower().Contains(term));
        }

        // ⚠ The year a rotation belongs to is READ, never inferred from its dates.
        //
        // The schema already states it, and states it totally: ServicePeriod.InternshipAssignmentId
        // and InternshipAssignment.RegistrationId and Registration.AcademicYearId are all NOT NULL,
        // with a RESTRICT foreign key onto AcademicYears. So every period has exactly one academic
        // year, structurally — a partition, with no row that could fall outside or into two.
        //
        // Comparing dates against the year's calendar span was an inference standing in for that
        // fact, and it disagreed with it. Measured 2026-08-30 on the live base: **7 030 of 105 626
        // periods (6.7%) land in a different year under the two rules**, and the registration is
        // right every time — 5 043 are 2019-2020 stages that ran into 2020-2021 because that year
        // was postponed, 1 841 are 2024-2025 stages finishing after 31 august. Dates cannot tell a
        // year that ran late from the next year's work; the registration says which the faculty
        // enrolled the student for, and that is the question being asked.
        //
        // It is also what removes the case that was reported: 41 sixth-year Pédiatrie rotations,
        // registered 2025-2026 and run 08 jul → 08 sep 2026, appeared under 2026-2027 for a
        // promotion with no planning that year. Their registration always said 2025-2026.
        if (academicYearId is { } yearId)
            query = query.Where(p => p.InternshipAssignment.Registration.AcademicYearId == yearId);

        return query.Where(ServicePeriodLifecycle.Predicate(state));
    }

    /// <summary>
    /// The slice, ordered. Chronological while it describes a plan or work in progress; most recent
    /// first once it describes the past, which is the end of an archive anybody actually opens.
    /// ⚠ The <c>Id</c> tiebreak is not cosmetic — a page boundary falling inside a window of 50
    /// students who all share one start date drops and duplicates rows without a total order.
    /// </summary>
    internal static IQueryable<ServicePeriod> OrderedScopedQuery(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<int> serviceIds,
        ServicePeriodState state,
        int? academicYearId = null,
        string? searchTerm = null)
    {
        var query = ScopedQuery(dbContext, serviceIds, state, academicYearId, searchTerm);

        return state is ServicePeriodState.AwaitingEvaluation or ServicePeriodState.Settled
            ? query.OrderByDescending(p => p.StartDate).ThenBy(p => p.Id)
            : query.OrderBy(p => p.StartDate).ThenBy(p => p.Id);
    }

    /// <summary>
    /// The size of all four slices under the caller's own filters — the search included, so a chef
    /// searching one student is told which slices that student is in rather than how big the slices
    /// would be without him.
    /// </summary>
    private async Task<ChefWorklistCounts> CountAsync(
        List<int> serviceIds, int? academicYearId, string? searchTerm, CancellationToken ct)
    {
        Task<int> CountOf(ServicePeriodState state) =>
            ScopedQuery(dbContext, serviceIds, state, academicYearId, searchTerm).CountAsync(ct);

        return new ChefWorklistCounts(
            await CountOf(ServicePeriodState.Planned),
            await CountOf(ServicePeriodState.Underway),
            await CountOf(ServicePeriodState.AwaitingEvaluation),
            await CountOf(ServicePeriodState.Settled));
    }

    /// <summary>
    /// The year to bound the worklist by: the one asked for, else the one flagged current, and none
    /// at all when the caller says <c>AllYears</c>. An id and nothing more — a period carries its
    /// year through its registration, so no caller here needs the year's dates.
    ///
    /// <para>⚠ Null — no year predicate — is also what an unresolvable year gives, and that is the
    /// deliberate direction to fail in. A chef's worklist is the screen that has to show live work;
    /// letting a missing <c>IsCurrent</c> flag, or a year id that no longer exists, empty it is
    /// exactly the silent blanking this whole area has been bitten by twice. Showing too much is
    /// visible and recoverable in one click; showing nothing is neither.</para>
    ///
    /// <para>Resolved here rather than through <c>AcademicYearResolver</c> for that reason: the
    /// resolver's contract is to fail when no year can be named, which is right for a handler that
    /// writes and wrong for the one read that must survive it. The span is needed either way, so
    /// this is one query, not the resolver's plus a lookup.</para>
    /// </summary>
    private async Task<int?> ResolveYearAsync(GetMyServicePeriodsQuery request, CancellationToken ct)
    {
        if (request.AllYears)
            return null;

        var years = dbContext.AcademicYears.AsNoTracking();

        var candidate = request.AcademicYearId is { } id
            ? years.Where(y => y.Id == id)
            : years.Where(y => y.IsCurrent);

        return await candidate.Select(y => (int?)y.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// How many more periods of the requested state the year filter is hiding. Counted as the same
    /// slice with the window taken off, minus the slice as returned — so it is the same predicate
    /// twice and cannot describe a different set of rows than the page does.
    ///
    /// <para>One extra count, and only while a year is applied. That is the price of making a
    /// year-scoped worklist honest, and it is the reason the scoping is safe to have at all.</para>
    /// </summary>
    private async Task<int> OutsideYearCountAsync(
        List<int> serviceIds,
        GetMyServicePeriodsQuery request,
        int? academicYearId,
        ChefWorklistCounts counts,
        CancellationToken ct)
    {
        if (academicYearId is null)
            return 0;

        int everywhere = await ScopedQuery(
                dbContext, serviceIds, request.EffectiveState, searchTerm: request.SearchTerm)
            .CountAsync(ct);

        return everywhere - counts.For(request.EffectiveState);
    }

    // ─── The page ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One page of real, published periods. A period whose generating cohort no longer matches the
    /// assignment's current cohort belongs to a student who transferred out after publish — flagged
    /// <see cref="TransferDirection.Outgoing"/> with the destination (current) group and the service
    /// the student now sits in for that window.
    ///
    /// <para>⚠ <b>Paginated, and it has to be.</b> This used to return every matching row on the
    /// grounds that the client groups them (window → group → student) and a page boundary would cut
    /// a group in half. It does — and the price of not paging was 3 220 rows per service card
    /// mounted at once, which is the crash this fixes. The grouping is now per page, and the slice
    /// keeps the payload small enough that one page is usually the whole slice anyway.</para>
    /// </summary>
    private async Task<PaginatedResponse<ServicePeriodResponse>> LoadPageAsync(
        List<int> serviceIds, GetMyServicePeriodsQuery request, int? academicYearId, CancellationToken ct)
    {
        var page = await OrderedScopedQuery(
                dbContext, serviceIds, request.EffectiveState, academicYearId, request.SearchTerm)
            .ToPaginatedResponseAsync(
                request.EffectivePageNumber,
                request.EffectivePageSize,
                p => new WorklistRow(
                    p.Id,
                    p.InternshipAssignmentId,
                    (p.InternshipAssignment.Registration.Student.FirstName ?? "") + " " +
                    (p.InternshipAssignment.Registration.Student.LastName ?? ""),
                    p.InternshipAssignment.Registration.Student.CNE,
                    p.InternshipAssignment.Registration.Student.Appogee,
                    p.ServiceId,
                    p.Service.Name,
                    p.Service.Hospital.Name,
                    p.StartDate,
                    p.EndDate,
                    p.IsComplete,
                    p.IsStarted,
                    p.IsInterrupted,
                    p.IsPaused,
                    p.Pauses.Where(x => x.ResumeDate == null).Select(x => x.Reason).FirstOrDefault(),
                    p.Evaluation != null,
                    p.CohortSlotAssignment != null
                        ? p.CohortSlotAssignment.Cohort.AcademicGroup.Label
                        : p.InternshipAssignment.Cohort.AcademicGroup.Label,
                    p.InternshipAssignment.Cohort.Stage.Name,
                    p.InternshipAssignment.Cohort.Stage.Level.Label,
                    p.CohortSlotAssignment != null ? (int?)p.CohortSlotAssignment.CohortId : null,
                    p.InternshipAssignment.CurrentCohortId,
                    p.InternshipAssignment.Cohort.AcademicGroup.Label,
                    p.InternshipAssignment.Cohort.SlotAssignments
                        .Where(sa => sa.StageSlot.StartDate == p.StartDate)
                        .Select(sa => sa.Service.Name)
                        .FirstOrDefault(),
                    p.InternshipAssignment.MembershipHistory
                        .Where(m => m.EndDate == null)
                        .Select(m => m.TransferReason)
                        .FirstOrDefault(),
                    p.InternshipAssignment.MembershipHistory
                        .Where(m => m.EndDate == null)
                        .Select(m => (DateOnly?)m.StartDate)
                        .FirstOrDefault()),
                ct);

        var items = page.Items.Select(r =>
        {
            bool outgoing = r.PeriodCohortId.HasValue && r.PeriodCohortId.Value != r.CurrentCohortId;
            var marker = outgoing
                ? new TransferMarker(
                    TransferDirection.Outgoing,
                    r.CurrentGroupLabel,
                    r.DestinationServiceName,
                    r.TransferReason,
                    r.TransferDate)
                : null;

            return new ServicePeriodResponse(
                r.Id,
                r.InternshipAssignmentId,
                r.FullName,
                r.Cne,
                r.Appogee,
                r.ServiceId,
                r.ServiceName,
                r.HospitalName,
                r.StartDate,
                r.EndDate,
                r.IsComplete,
                r.HasEvaluation,
                r.RosterGroupLabel,
                r.StageName,
                r.LevelLabel,
                marker,
                r.IsPaused,
                r.PauseReason,
                r.IsInterrupted,
                ServicePeriodLifecycle.StateOf(
                    r.IsStarted, r.IsComplete, r.IsInterrupted, r.HasEvaluation));
        }).ToList();

        return new PaginatedResponse<ServicePeriodResponse>(
            items, page.PageNumber, page.PageSize, page.TotalCount);
    }

    /// <summary>
    /// The flat row the page projects to. Named rather than anonymous so the projection is an
    /// expression <c>ToPaginatedResponseAsync</c> can take, and so the shape has one definition.
    /// </summary>
    private sealed record WorklistRow(
        Guid Id,
        Guid InternshipAssignmentId,
        string FullName,
        string? Cne,
        string Appogee,
        int ServiceId,
        string ServiceName,
        string HospitalName,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsComplete,
        bool IsStarted,
        bool IsInterrupted,
        bool IsPaused,
        string? PauseReason,
        bool HasEvaluation,
        string RosterGroupLabel,
        string StageName,
        string? LevelLabel,
        int? PeriodCohortId,
        int CurrentCohortId,
        string CurrentGroupLabel,
        string? DestinationServiceName,
        string? TransferReason,
        DateOnly? TransferDate);

    /// <summary>
    /// Students who transferred into the chef's services but whose periods were never
    /// re-published, so no real <see cref="ServicePeriod"/> exists. Synthesized
    /// from the current cohort's slot assignments that land in a chef service, for assignments
    /// whose active membership records a transfer and have no matching published period yet.
    /// Flagged <see cref="TransferDirection.Incoming"/> with the origin group/service.
    /// </summary>
    private async Task<List<ServicePeriodResponse>> LoadIncomingTransfersAsync(
        List<int> chefServiceIds, int? academicYearId, CancellationToken ct)
    {
        var rows = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => chefServiceIds.Contains(sa.ServiceId))
            .SelectMany(sa => sa.Cohort.Assignments, (sa, a) => new { sa, a })
            // Scoped through the assignment's registration, exactly as the periods are. An overlay
            // answering "which year?" a different way would put a student on a page from which his
            // own rotation had been filtered off.
            .Where(x => academicYearId == null || x.a.Registration.AcademicYearId == academicYearId)
            .Where(x => x.a.MembershipHistory.Any(m => m.EndDate == null && m.TransferReason != null))
            // No synthesized "incoming" row once a real period is materialised against this slot
            // (a forced mid-stage hand-off re-creates the period dated from the transfer day, so
            // match on the slot cell rather than the start date).
            .Where(x => !x.a.ServicePeriods.Any(p => p.CohortSlotAssignmentId == x.sa.Id))
            .Select(x => new
            {
                AssignmentId = x.a.Id,
                FullName = (x.a.Registration.Student.FirstName ?? "") + " " +
                           (x.a.Registration.Student.LastName ?? ""),
                Cne = x.a.Registration.Student.CNE,
                Appogee = x.a.Registration.Student.Appogee,
                x.sa.ServiceId,
                ServiceName = x.sa.Service.Name,
                HospitalName = x.sa.Service.Hospital.Name,
                StartDate = x.sa.StageSlot.StartDate,
                EndDate = x.sa.StageSlot.EndDate,
                CurrentGroupLabel = x.sa.Cohort.AcademicGroup.Label,
                StageName = x.sa.Cohort.Stage.Name,
                LevelLabel = x.sa.Cohort.Stage.Level.Label,
                TransferReason = x.a.MembershipHistory
                    .Where(m => m.EndDate == null)
                    .Select(m => m.TransferReason)
                    .FirstOrDefault(),
                TransferDate = x.a.MembershipHistory
                    .Where(m => m.EndDate == null)
                    .Select(m => (DateOnly?)m.StartDate)
                    .FirstOrDefault(),
                OriginGroupLabel = x.a.MembershipHistory
                    .Where(m => m.EndDate != null)
                    .OrderByDescending(m => m.EndDate)
                    .Select(m => m.Cohort.AcademicGroup.Label)
                    .FirstOrDefault(),
                OriginServiceName = x.a.MembershipHistory
                    .Where(m => m.EndDate != null)
                    .OrderByDescending(m => m.EndDate)
                    .Select(m => m.Cohort.SlotAssignments
                        .Where(sa2 => sa2.StageSlot.StartDate == x.sa.StageSlot.StartDate)
                        .Select(sa2 => sa2.Service.Name)
                        .FirstOrDefault())
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ServicePeriodResponse(
            Guid.Empty,
            r.AssignmentId,
            r.FullName,
            r.Cne,
            r.Appogee,
            r.ServiceId,
            r.ServiceName,
            r.HospitalName,
            r.StartDate,
            r.EndDate,
            IsComplete: false,
            HasEvaluation: false,
            r.CurrentGroupLabel,
            r.StageName,
            r.LevelLabel,
            new TransferMarker(
                TransferDirection.Incoming,
                r.OriginGroupLabel ?? "—",
                r.OriginServiceName,
                r.TransferReason,
                r.TransferDate),
            // Synthesized from a slot cell, so there is no period to be in a state: it is shown
            // beside the open rotations because that is where the chef needs to see the arrival.
            State: ServicePeriodState.Underway)).ToList();
    }
}
