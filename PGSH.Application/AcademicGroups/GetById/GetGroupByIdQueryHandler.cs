using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
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
                        r.Status.ToString()))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return group is null
            ? Result.Failure<GroupDetailResponse>(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.Id}' was not found."))
            : group;
    }
}
