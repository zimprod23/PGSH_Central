using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GetById;

internal sealed class GetGroupByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetGroupByIdQuery, GroupDetailResponse>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        GetGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var group = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.Id)
            .Select(g => new GroupDetailResponse(
                g.Id,
                g.Label,
                g.GroupNumber,
                g.GeographicZone,
                g.RotationGroup,
                g.AcademicYearId,
                g.AcademicYear.Label,
                g.Registrations
                    .OrderBy(r => r.Student.LastName)
                    .Select(r => new GroupStudentResponse(
                        r.Id,
                        r.StudentId,
                        (r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? ""),
                        r.Student.CNE ?? "",
                        r.Student.Email ?? "",
                        r.Status.ToString(),
                        // An active temporary loan moved this stage's assignment to another group;
                        // the current cohort therefore sits in the destination group.
                        r.InternshipAssignments
                            .Where(a => a.MembershipHistory.Any(m =>
                                m.EndDate == null && m.TransferType == TransferType.Temporary))
                            .Select(a => a.Cohort.AcademicGroup.Label)
                            .FirstOrDefault(),
                        r.InternshipAssignments
                            .Where(a => a.MembershipHistory.Any(m =>
                                m.EndDate == null && m.TransferType == TransferType.Temporary))
                            .Select(a => a.Cohort.Stage.Name)
                            .FirstOrDefault()))
                    .ToList(),
                // Students from other groups currently loaned INTO this group for one stage.
                dbContext.InternshipAssignments
                    .Where(a => a.Cohort.AcademicGroupId == g.Id
                             && a.Registration.AcademicGroupId != g.Id
                             && a.MembershipHistory.Any(m =>
                                 m.EndDate == null && m.TransferType == TransferType.Temporary))
                    .Select(a => new IncomingLoanResponse(
                        a.Registration.StudentId,
                        (a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? ""),
                        a.Registration.Student.CNE ?? "",
                        a.Registration.AcademicGroup!.Label,
                        a.Cohort.Stage.Name))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return group is null
            ? Result.Failure<GroupDetailResponse>(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.Id}' was not found."))
            : group;
    }
}
