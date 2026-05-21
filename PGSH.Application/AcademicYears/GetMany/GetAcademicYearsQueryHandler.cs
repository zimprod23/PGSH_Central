using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears.GetMany;

internal sealed class GetAcademicYearsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetAcademicYearsQuery, List<AcademicYearResponse>>
{
    public async Task<Result<List<AcademicYearResponse>>> Handle(
        GetAcademicYearsQuery request, CancellationToken ct)
    {
        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .OrderByDescending(y => y.StartDate)
            .Select(y => new AcademicYearResponse(y.Id, y.Label, y.IsCurrent))
            .ToListAsync(ct);

        return years;
    }
}
