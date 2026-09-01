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
        var versions = await VersionRowsQuery(dbContext, request.Program).ToListAsync(cancellationToken);

        return versions;
    }

    /// <summary>
    /// ⚠ Two aggregates ride in the projection — the recorded levels and the students stamped with
    /// the text. Both compile to correlated scalar subqueries, which the provider accepts (unlike a
    /// collection subquery over a computed element, the shape that killed the macro plan), and both
    /// are bounded by the handful of rows this table holds. Named so <c>SqlTranslationTests</c> can
    /// say so rather than leaving it to the first request.
    /// </summary>
    internal static IQueryable<CnpnVersionResponse> VersionRowsQuery(
        IApplicationDbContext dbContext, AcademicProgram? program) =>
        dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => program == null || v.AcademicProgram == program)
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
                dbContext.Students.Count(s => s.CnpnVersionId == v.Id)));
}
