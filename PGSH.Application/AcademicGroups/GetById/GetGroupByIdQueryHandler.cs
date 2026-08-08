using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetById;

internal sealed class GetGroupByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetGroupByIdQuery, GroupDetailResponse>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        GetGroupByIdQuery request, CancellationToken cancellationToken)
    {
        // Header first, on its own. Nesting the roster inside this projection made one query whose
        // cost scaled with the group's size even when the caller only wanted the group's name.
        var header = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.Id)
            .Select(g => new
            {
                g.Id,
                g.Label,
                g.GroupNumber,
                g.GeographicZone,
                g.RotationGroup,
                g.AcademicYearId,
                AcademicYearLabel = g.AcademicYear.Label,
                StudentCount = g.Registrations.Count,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
            return Result.Failure<GroupDetailResponse>(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.Id}' was not found."));

        var roster = dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicGroupId == request.Id);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            roster = roster.Where(r =>
                (r.Student.FirstName ?? "").ToLower().Contains(term)
             || (r.Student.LastName ?? "").ToLower().Contains(term)
             || (r.Student.CNE ?? "").ToLower().Contains(term)
             || (r.Student.Appogee ?? "").ToLower().Contains(term)
             || (r.Student.Email ?? "").ToLower().Contains(term));
        }

        var students = await roster
            .OrderBy(r => r.Student.LastName)
            .ThenBy(r => r.Student.FirstName)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                r => new GroupStudentResponse(
                    r.Id,
                    r.StudentId,
                    (r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? ""),
                    r.Student.CNE ?? "",
                    r.Student.Email ?? "",
                    r.Status.ToString(),
                    // An active temporary loan moved this stage's assignment to another group; the
                    // current cohort therefore sits in the destination group. These two correlated
                    // lookups are why the roster has to be paged — they run per student returned.
                    r.InternshipAssignments
                        .Where(a => a.MembershipHistory.Any(m =>
                            m.EndDate == null && m.TransferType == TransferType.Temporary))
                        .Select(a => a.Cohort.AcademicGroup.Label)
                        .FirstOrDefault(),
                    r.InternshipAssignments
                        .Where(a => a.MembershipHistory.Any(m =>
                            m.EndDate == null && m.TransferType == TransferType.Temporary))
                        .Select(a => a.Cohort.Stage.Name)
                        .FirstOrDefault()),
                cancellationToken);

        // Students from other groups currently loaned INTO this group for one stage. Bounded by the
        // number of live loans, not by the group's size, so it is returned whole.
        var incomingLoans = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.AcademicGroupId == request.Id
                     && a.Registration.AcademicGroupId != request.Id
                     && a.MembershipHistory.Any(m =>
                         m.EndDate == null && m.TransferType == TransferType.Temporary))
            .Select(a => new IncomingLoanResponse(
                a.Registration.StudentId,
                (a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? ""),
                a.Registration.Student.CNE ?? "",
                a.Registration.AcademicGroup!.Label,
                a.Cohort.Stage.Name))
            .ToListAsync(cancellationToken);

        return new GroupDetailResponse(
            header.Id,
            header.Label,
            header.GroupNumber,
            header.GeographicZone,
            header.RotationGroup,
            header.AcademicYearId,
            header.AcademicYearLabel,
            header.StudentCount,
            students,
            incomingLoans);
    }
}
