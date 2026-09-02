using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetMany;

internal sealed class GetStudentsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetStudentsQuery, PaginatedResponse<StudentSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StudentSummaryResponse>>> Handle(
        GetStudentsQuery request, CancellationToken ct)
    {
        IQueryable<Student> query = context.Students.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.CNE))
            query = query.Where(s => s.CNE == request.CNE);

        if (!string.IsNullOrWhiteSpace(request.Appogee))
            query = query.Where(s => s.Appogee == request.Appogee);

        if (!string.IsNullOrWhiteSpace(request.CIN))
            query = query.Where(s => s.CIN == request.CIN);

        if (request.Program.HasValue)
            query = query.Where(s => s.AcademicProgram == request.Program.Value);

        // A year narrows the population, not just the columns. Projecting the year's registration
        // while still returning students who have none listed the whole imported history under
        // whichever year was selected, every row blank past the name — and made the dashboard's
        // "étudiants inscrits" a count of everyone ever enrolled rather than of this promotion.
        int? yearId = request.AcademicYearId;

        if (yearId.HasValue)
            query = query.Where(s => s.Registrations.Any(r => r.AcademicYearId == yearId.Value));

        // ⚠ **One `Any`, not two.** A level and a year each satisfied by a *different* registration is
        // not a student of that promotion — and every student past his second year has one. Asked as
        // two conditions, « inscrit en 2026-2027 » ∧ « a été en 3ᵉ année (un jour) » returns the 7ᵉ
        // année student who sat in the 3ᵉ in 2021; 2 635 students in this base have repeated, and every
        // one of them is a false positive. The pair has to hold on a single registration row.
        //
        // A level with no year stays meaningful — "everyone who has ever been in the 3ᵉ année" — which
        // is what the omitted year already means for the list as a whole, so it is left reachable
        // rather than resolved to the current year here.
        //
        // ⚠ The **status** joins that same `Any` for the identical reason, and it is the stricter
        // case: a verdict is a fact about one year. « Diplômé » ∧ « 2026-2027 » asked separately
        // returns every student who ever graduated *and* happens to hold a 2026-2027 registration —
        // which, for a thesis year re-registered every September, is most of them.
        int? levelId = request.LevelId;
        RegistrationStatus? status = request.Status;

        if (levelId.HasValue || status.HasValue)
            query = query.Where(s => s.Registrations.Any(r =>
                (levelId == null || r.LevelId == levelId.Value) &&
                (status == null || r.Status == status.Value) &&
                (yearId == null || r.AcademicYearId == yearId.Value)));

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            // Trimmed and lowered on both sides: a pasted CNE carries stray spaces, and Appogee was
            // previously matched case-sensitively so "ap12" never found "AP12".
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s =>
                s.FirstName.ToLower().Contains(term) ||
                s.LastName.ToLower().Contains(term)  ||
                s.Email.ToLower().Contains(term)     ||
                (s.CNE ?? "").ToLower().Contains(term) ||
                s.Appogee.ToLower().Contains(term)   ||
                (s.CIN != null && s.CIN.ToLower().Contains(term)));
        }

        var response = await query
            .OrderBy(s => s.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new StudentSummaryResponse(
                    s.Id, s.Email, s.FirstName, s.LastName, s.CNE, s.Appogee, s.AcademicProgram.ToString(), s.CIN,
                    // The selected academic year's registration when a year is given (at most one),
                    // otherwise the most recent registration.
                    s.Registrations
                        .Where(r => yearId == null || r.AcademicYearId == yearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.Level.Label)
                        .FirstOrDefault(),
                    s.Registrations
                        .Where(r => yearId == null || r.AcademicYearId == yearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.AcademicGroup != null ? r.AcademicGroup.Label : null)
                        .FirstOrDefault(),
                    s.Registrations
                        .Where(r => yearId == null || r.AcademicYearId == yearId)
                        .OrderByDescending(r => r.AcademicYear.StartDate)
                        .Select(r => r.Status.ToString())
                        .FirstOrDefault()),
                ct);

        return Result.Success(response);
    }
}
