using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.ServicePeriods;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

internal sealed class GetMyServicePeriodsQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetMyServicePeriodsQuery, PaginatedResponse<ServicePeriodResponse>>
{
    public async Task<Result<PaginatedResponse<ServicePeriodResponse>>> Handle(
        GetMyServicePeriodsQuery request, CancellationToken cancellationToken)
    {
        var chefServiceIds = await authorizer.ChefServiceIdsAsync(cancellationToken);

        if (request.ServiceId.HasValue)
            chefServiceIds = chefServiceIds.Where(id => id == request.ServiceId.Value).ToList();

        if (chefServiceIds.Count == 0)
            return new PaginatedResponse<ServicePeriodResponse>([], 1, 1, 0);

        // Deliberately NOT year-scoped by default. A chef's worklist is "what is live in my services",
        // which IsStarted already expresses. Defaulting to the current academic year couples the
        // worklist to a bookkeeping record that drifts out of step with the actual rotation dates —
        // when it does, the chef silently sees nothing at all, which is far worse than showing an
        // extra past window. Year scoping stays available, but only when the caller asks for it.
        var window = request.AcademicYearId.HasValue
            ? await ResolveYearWindowAsync(request.AcademicYearId.Value, cancellationToken)
            : null;

        var items = new List<ServicePeriodResponse>();
        items.AddRange(await LoadPublishedPeriodsAsync(chefServiceIds, request.IsComplete, window, cancellationToken));
        items.AddRange(await LoadIncomingTransfersAsync(chefServiceIds, request.IsComplete, window, cancellationToken));

        // The chef worklist is grouped client-side (period → group → students), so it must
        // return every matching row. Paginating here would silently drop students from
        // groups whose periods fall past the page boundary — a service can hold 160+ periods.
        var ordered = items.OrderBy(p => p.StartDate).ToList();
        return new PaginatedResponse<ServicePeriodResponse>(ordered, 1, Math.Max(ordered.Count, 1), ordered.Count);
    }

    /// <summary>The calendar span of an academic year — what a rotation is scoped against.</summary>
    private sealed record YearWindow(DateOnly StartDate, DateOnly EndDate);

    /// <summary>
    /// The date span of the requested year. Null when no such year exists, in which case the
    /// worklist is not year-scoped rather than empty.
    /// </summary>
    private Task<YearWindow?> ResolveYearWindowAsync(int academicYearId, CancellationToken ct) =>
        dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == academicYearId)
            .Select(y => new YearWindow(y.StartDate, y.EndDate))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Real, published periods in the chef's services. A period whose generating cohort no
    /// longer matches the assignment's current cohort belongs to a student who transferred
    /// out after publish — flagged <see cref="TransferDirection.Outgoing"/> with the
    /// destination (current) group and the service the student now sits in for that window.
    /// </summary>
    private async Task<List<ServicePeriodResponse>> LoadPublishedPeriodsAsync(
        List<int> chefServiceIds, bool? isComplete, YearWindow? window, CancellationToken ct)
    {
        var query = dbContext.ServicePeriods
            .AsNoTracking()
            // Only started periods are the chef's concern; future, not-yet-started rotations stay
            // hidden. A period cut short by a mid-stage transfer is kept as a read-only "parti vers …"
            // history row (flagged IsInterrupted) so the origin chef sees where the student went but
            // cannot act on it — it is terminal, never counted as work nor evaluable.
            .Where(p => chefServiceIds.Contains(p.ServiceId) && p.IsStarted);

        if (isComplete.HasValue)
            query = query.Where(p => p.IsComplete == isComplete.Value);

        // Scope on when the rotation actually runs, not on the academic year its registration
        // carries: a stage planned across the year boundary (or a student registered in an earlier
        // year serving a retake now) is still current work for the chef standing in the service.
        if (window is not null)
        {
            var (yearStart, yearEnd) = window;
            query = query.Where(p => p.StartDate <= yearEnd && p.EndDate >= yearStart);
        }

        var rows = await query
            .Select(p => new
            {
                p.Id,
                p.InternshipAssignmentId,
                FullName = (p.InternshipAssignment.Registration.Student.FirstName ?? "") + " " +
                           (p.InternshipAssignment.Registration.Student.LastName ?? ""),
                Cne = p.InternshipAssignment.Registration.Student.CNE,
                Appogee = p.InternshipAssignment.Registration.Student.Appogee,
                p.ServiceId,
                ServiceName = p.Service.Name,
                HospitalName = p.Service.Hospital.Name,
                p.StartDate,
                p.EndDate,
                p.IsComplete,
                p.IsInterrupted,
                p.IsPaused,
                PauseReason = p.Pauses
                    .Where(x => x.ResumeDate == null)
                    .Select(x => x.Reason)
                    .FirstOrDefault(),
                HasEvaluation = p.Evaluation != null,
                RosterGroupLabel = p.CohortSlotAssignment != null
                    ? p.CohortSlotAssignment.Cohort.AcademicGroup.Label
                    : p.InternshipAssignment.Cohort.AcademicGroup.Label,
                StageName = p.InternshipAssignment.Cohort.Stage.Name,
                LevelLabel = p.InternshipAssignment.Cohort.Stage.Level.Label,
                PeriodCohortId = p.CohortSlotAssignment != null ? (int?)p.CohortSlotAssignment.CohortId : null,
                CurrentCohortId = p.InternshipAssignment.CurrentCohortId,
                CurrentGroupLabel = p.InternshipAssignment.Cohort.AcademicGroup.Label,
                DestinationServiceName = p.InternshipAssignment.Cohort.SlotAssignments
                    .Where(sa => sa.StageSlot.StartDate == p.StartDate)
                    .Select(sa => sa.Service.Name)
                    .FirstOrDefault(),
                TransferReason = p.InternshipAssignment.MembershipHistory
                    .Where(m => m.EndDate == null)
                    .Select(m => m.TransferReason)
                    .FirstOrDefault(),
                TransferDate = p.InternshipAssignment.MembershipHistory
                    .Where(m => m.EndDate == null)
                    .Select(m => (DateOnly?)m.StartDate)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r =>
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
                r.IsInterrupted);
        }).ToList();
    }

    /// <summary>
    /// Students who transferred into the chef's services but whose periods were never
    /// re-published, so no real <see cref="Domain.Stages.ServicePeriod"/> exists. Synthesized
    /// from the current cohort's slot assignments that land in a chef service, for assignments
    /// whose active membership records a transfer and have no matching published period yet.
    /// Flagged <see cref="TransferDirection.Incoming"/> with the origin group/service.
    /// </summary>
    private async Task<List<ServicePeriodResponse>> LoadIncomingTransfersAsync(
        List<int> chefServiceIds, bool? isComplete, YearWindow? window, CancellationToken ct)
    {
        // Incoming rows are never "complete" — exclude them when the caller filters to completed periods.
        if (isComplete == true)
            return [];

        var slots = dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => chefServiceIds.Contains(sa.ServiceId));

        if (window is not null)
        {
            var (yearStart, yearEnd) = window;
            slots = slots.Where(sa => sa.StageSlot.StartDate <= yearEnd && sa.StageSlot.EndDate >= yearStart);
        }

        var rows = await slots
            .SelectMany(sa => sa.Cohort.Assignments, (sa, a) => new { sa, a })
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
                r.TransferDate))).ToList();
    }
}
