using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Effectivity;

/// <summary>
/// The effectivity rules on record, optionally for one text or one programme.
/// </summary>
/// <remarks>
/// Not paginated, and it is bounded by construction rather than by hope: a rule is unique per
/// (text, level), so the table can hold at most one row per text per year of study — five texts of a
/// seven-year programme is thirty-five rows, and the whole faculty is under a hundred. If that ever
/// stops being true the constraint has changed, not the volume.
/// </remarks>
public sealed record GetCnpnEffectivitiesQuery(
    int? CnpnVersionId = null,
    AcademicProgram? Program = null) : IQuery<IReadOnlyList<CnpnEffectivityResponse>>;

internal sealed class GetCnpnEffectivitiesQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCnpnEffectivitiesQuery, IReadOnlyList<CnpnEffectivityResponse>>
{
    public async Task<Result<IReadOnlyList<CnpnEffectivityResponse>>> Handle(
        GetCnpnEffectivitiesQuery request, CancellationToken ct)
    {
        var rows = await EffectivityRowsQuery(dbContext, request.CnpnVersionId, request.Program)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<CnpnEffectivityResponse>>(rows);
    }

    /// <summary>
    /// ⚠ The registration count is a correlated scalar subquery in the projection. It is what the
    /// rule has actually done, and it is compared on <c>StartDate</c> — named so its translation is
    /// pinned rather than discovered on the effectivity page.
    /// </summary>
    internal static IQueryable<CnpnEffectivityResponse> EffectivityRowsQuery(
        IApplicationDbContext dbContext, int? cnpnVersionId, AcademicProgram? program) =>
        dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => cnpnVersionId == null || e.CnpnVersionId == cnpnVersionId)
            .Where(e => program == null || e.CnpnVersion.AcademicProgram == program)
            .OrderBy(e => e.CnpnVersion.AcademicProgram)
            .ThenBy(e => e.FromAcademicYear.StartDate)
            .ThenBy(e => e.Level.Year)
            .Select(e => new CnpnEffectivityResponse(
                e.Id,
                e.CnpnVersionId,
                e.CnpnVersion.Code,
                e.CnpnVersion.Label,
                e.CnpnVersion.AcademicProgram,
                e.LevelId,
                e.Level.Label ?? string.Empty,
                e.Level.Year,
                e.FromAcademicYearId,
                e.FromAcademicYear.Label,
                e.Note,
                e.RecordedOn,
                // What the rule has actually done. An authored rule with zero registrations behind it
                // has not fired yet — which is the normal state right after it is recorded, and a very
                // different thing from one that fired and moved four hundred students.
                dbContext.Registrations.Count(r =>
                    r.LevelId == e.LevelId
                 && r.CnpnVersionId == e.CnpnVersionId
                 && r.AcademicYear.StartDate >= e.FromAcademicYear.StartDate)));
}
