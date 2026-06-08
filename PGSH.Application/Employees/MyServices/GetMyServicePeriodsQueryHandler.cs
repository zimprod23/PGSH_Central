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

        var items = new List<ServicePeriodResponse>();
        items.AddRange(await LoadPublishedPeriodsAsync(chefServiceIds, request.IsComplete, cancellationToken));
        items.AddRange(await LoadIncomingTransfersAsync(chefServiceIds, request.IsComplete, cancellationToken));

        // The chef worklist is grouped client-side (period → group → students), so it must
        // return every matching row. Paginating here would silently drop students from
        // groups whose periods fall past the page boundary — a service can hold 160+ periods.
        var ordered = items.OrderBy(p => p.StartDate).ToList();
        return new PaginatedResponse<ServicePeriodResponse>(ordered, 1, Math.Max(ordered.Count, 1), ordered.Count);
    }

    /// <summary>
    /// Real, published periods in the chef's services. A period whose generating cohort no
    /// longer matches the assignment's current cohort belongs to a student who transferred
    /// out after publish — flagged <see cref="TransferDirection.Outgoing"/> with the
    /// destination (current) group and the service the student now sits in for that window.
    /// </summary>
    private async Task<List<ServicePeriodResponse>> LoadPublishedPeriodsAsync(
        List<int> chefServiceIds, bool? isComplete, CancellationToken ct)
    {
        var query = dbContext.ServicePeriods
            .AsNoTracking()
            // Only started periods are the chef's concern; future, not-yet-started rotations stay hidden.
            .Where(p => chefServiceIds.Contains(p.ServiceId) && p.IsStarted);

        if (isComplete.HasValue)
            query = query.Where(p => p.IsComplete == isComplete.Value);

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
                HasEvaluation = p.Evaluation != null,
                RosterGroupLabel = p.CohortSlotAssignment != null
                    ? p.CohortSlotAssignment.Cohort.AcademicGroup.Label
                    : p.InternshipAssignment.Cohort.AcademicGroup.Label,
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
                marker);
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
        List<int> chefServiceIds, bool? isComplete, CancellationToken ct)
    {
        // Incoming rows are never "complete" — exclude them when the caller filters to completed periods.
        if (isComplete == true)
            return [];

        var rows = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(sa => chefServiceIds.Contains(sa.ServiceId))
            .SelectMany(sa => sa.Cohort.Assignments, (sa, a) => new { sa, a })
            .Where(x => x.a.MembershipHistory.Any(m => m.EndDate == null && m.TransferReason != null))
            .Where(x => !x.a.ServicePeriods.Any(p =>
                p.ServiceId == x.sa.ServiceId && p.StartDate == x.sa.StageSlot.StartDate))
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
            new TransferMarker(
                TransferDirection.Incoming,
                r.OriginGroupLabel ?? "—",
                r.OriginServiceName,
                r.TransferReason,
                r.TransferDate))).ToList();
    }
}
