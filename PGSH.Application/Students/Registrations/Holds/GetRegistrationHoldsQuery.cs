using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Extensions;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Holds;

/// <summary>
/// The worklist: every registration currently withdrawn from planning, and why.
/// </summary>
/// <remarks>
/// <para>This is the screen the whole mechanism exists for. Holding a registration is only half an
/// answer — the other half is being able to walk the list one student at a time and clear it, which
/// is what « on les ajuste manuellement depuis l'application » means. Without this page the flag is
/// a silent exclusion, which is the failure it was built to remove.</para>
///
/// <para>⚠ <b>Paginated, and scoped to one academic year by default.</b> The 2026-2027 roll raises
/// ~1 450 holds in one act — 1 267 absentees plus 182 final-year debts — so the unbounded list is the
/// browser-killing shape this codebase has met four times already. The year is resolved through
/// <see cref="AcademicYearResolver"/> like every other year-scoped read: an omitted year is the
/// current one, never all of them.</para>
///
/// <para>⚠ <b>The year is the <em>registration's</em>, read from the schema</b>
/// (<c>Registration.AcademicYearId</c>), never inferred from <c>RaisedOn</c>. The roll for 2026-2027
/// raises holds on 2025-2026 registrations and creates 2026-2027 ones in the same act, so the date
/// the flag was written says nothing about which promotion it belongs to.</para>
/// </remarks>
/// <param name="Filter">
/// Defaults to <see cref="RegistrationHoldFilter.Active"/> — the students still frozen. The released
/// ones are the audit trail and are asked for explicitly.
/// </param>
public sealed record GetRegistrationHoldsQuery(
    int? AcademicYearId = null,
    RegistrationHoldReason? Reason = null,
    RegistrationHoldFilter Filter = RegistrationHoldFilter.Active,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 25) : IQuery<PaginatedResponse<RegistrationHoldResponse>>;

internal sealed class GetRegistrationHoldsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetRegistrationHoldsQuery, PaginatedResponse<RegistrationHoldResponse>>
{
    public async Task<Result<PaginatedResponse<RegistrationHoldResponse>>> Handle(
        GetRegistrationHoldsQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PaginatedResponse<RegistrationHoldResponse>>(year.Error);

        var query = ScopedQuery(dbContext, year.Value, request.Reason, request.Filter);

        // ⚠ Every field lowered, not just the first. Appogee was case-sensitive for months because one
        // side of the comparison was left alone, so « ap2200a » never found AP2200A.
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();

            query = query.Where(h =>
                h.Registration.Student.LastName.ToLower().Contains(term) ||
                h.Registration.Student.FirstName.ToLower().Contains(term) ||
                (h.Registration.Student.CNE != null && h.Registration.Student.CNE.ToLower().Contains(term)) ||
                (h.Registration.Student.Appogee != null && h.Registration.Student.Appogee.ToLower().Contains(term)));
        }

        return await query
            // Oldest first: a hold that has been waiting longest is the one most likely to be holding
            // a promotion's planning up.
            .OrderBy(h => h.RaisedOn)
            .ThenBy(h => h.Registration.Student.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                h => new RegistrationHoldResponse(
                    h.Id,
                    h.RegistrationId,
                    h.Registration.StudentId,
                    h.Registration.Student.LastName + " " + h.Registration.Student.FirstName,
                    h.Registration.Student.CNE,
                    h.Registration.Student.Appogee,
                    h.Registration.Level.Label ?? "",
                    h.Registration.AcademicYear.Label,
                    h.Registration.Status,
                    h.Reason,
                    // Computed in the projection, which EF evaluates on the client by design — a
                    // method call here is safe where the same call in a Where would be refused.
                    h.Reason.Label(),
                    h.Evidence,
                    h.Reason.Remedy(),
                    h.Reason.BlocksPlanning(),
                    h.RaisedOn,
                    h.ReleasedOn,
                    h.ReleaseNote),
                cancellationToken);
    }

    /// <summary>
    /// One predicate for the page and for anything that later needs to count the same set, and named
    /// so <c>SqlTranslationTests</c> can compile it: a query buried in a private async method cannot
    /// be handed to <c>ToQueryString()</c>, and the in-memory provider translates nothing.
    /// </summary>
    internal static IQueryable<RegistrationHold> ScopedQuery(
        IApplicationDbContext dbContext,
        int academicYearId,
        RegistrationHoldReason? reason,
        RegistrationHoldFilter filter) =>
        dbContext.RegistrationHolds
            .AsNoTracking()
            .Where(h => h.Registration.AcademicYearId == academicYearId)
            .Where(h => reason == null || h.Reason == reason)
            .Where(h => filter == RegistrationHoldFilter.All
                     || (filter == RegistrationHoldFilter.Active && h.ReleasedOn == null)
                     || (filter == RegistrationHoldFilter.Released && h.ReleasedOn != null));
}
