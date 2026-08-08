using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.GetCnpnVersions;

/// <summary>
/// Every recorded CNPN, newest intake first. Deliberately unpaginated: a programme accumulates one
/// text every several years, so this is bounded by ministerial output, not by the faculty's size.
/// </summary>
public sealed record GetCnpnVersionsQuery(AcademicProgram? Program = null)
    : IQuery<IReadOnlyList<CnpnVersionResponse>>;

public sealed record CnpnVersionResponse(
    int     Id,
    string  Code,
    string  Label,
    string  AcademicProgram,
    int     TotalYears,
    string? Reference,
    int?    AppliesToEntrantsFromAcademicYearId,
    string? AppliesToEntrantsFromLabel,
    /// <summary>False for a text kept only for the record, which governs no intake.</summary>
    bool    GovernsAnIntake,
    /// <summary>Levels that already have a recorded requirement set under this text.</summary>
    int     LevelsRecorded,
    /// <summary>Students currently stamped with this text.</summary>
    int     StudentCount);

internal sealed class GetCnpnVersionsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCnpnVersionsQuery, IReadOnlyList<CnpnVersionResponse>>
{
    public async Task<Result<IReadOnlyList<CnpnVersionResponse>>> Handle(
        GetCnpnVersionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.CnpnVersions.AsNoTracking();

        if (request.Program.HasValue)
            query = query.Where(v => v.AcademicProgram == request.Program.Value);

        var versions = await query
            .OrderBy(v => v.AcademicProgram)
            .ThenByDescending(v => v.AppliesToEntrantsFromAcademicYear!.StartDate)
            .Select(v => new CnpnVersionResponse(
                v.Id,
                v.Code,
                v.Label,
                v.AcademicProgram.ToString(),
                v.TotalYears,
                v.Reference,
                v.AppliesToEntrantsFromAcademicYearId,
                v.AppliesToEntrantsFromAcademicYear != null
                    ? v.AppliesToEntrantsFromAcademicYear.Label
                    : null,
                v.AppliesToEntrantsFromAcademicYearId != null,
                v.Curricula.Count,
                dbContext.Students.Count(s => s.CnpnVersionId == v.Id)))
            .ToListAsync(cancellationToken);

        return versions;
    }
}
