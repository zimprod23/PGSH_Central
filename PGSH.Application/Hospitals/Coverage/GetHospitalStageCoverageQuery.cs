using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Coverage;

/// <summary>
/// Can one hospital host one promotion's whole rotation — and if not, exactly which stages it cannot?
///
/// <para><b>The question asked before a promise, not after it.</b> « Cet étudiant fait tous ses stages
/// à l'hôpital militaire » is answerable only against <c>Stage.AllowedServices</c>, the list a cell is
/// checked against before it may be written. Measured on the live catalogue 2026-09-03: the Hôpital
/// Militaire Mohammed V is the largest in the base (35 services) and carries one for every 6ᵉ année
/// stage, so the promise holds there — while in 5ᵉ année it covers six stages of seven, because
/// <b>Santé Publique authorises a single service and it is elsewhere</b>. Without this read that
/// contradiction surfaces at the sixth cell, after the student has been told yes.</para>
///
/// <para>It is the same question as <c>GetRosterPlacementsQuery</c> asked one step earlier: that one
/// says who <i>is</i> there, this one says who <i>could</i> be.</para>
/// </summary>
/// <remarks>
/// ⚠ <b>Deliberately not year-scoped.</b> <c>Stage</c>, <c>Service</c>, <c>Hospital</c> and the
/// allowed-services list are year-invariant catalogue — « Chirurgie » and « Service de Cardiologie »
/// outlive every promotion — so there is no year for this read to be wrong about. Adding one would
/// suggest the answer moves from September to September, which it does not.
/// </remarks>
public sealed record GetHospitalStageCoverageQuery(int HospitalId, int LevelId)
    : IQuery<HospitalStageCoverageResponse>;

/// <param name="CoveredStageCount">Stages with at least one authorised service at this hospital.</param>
/// <param name="UnauthoredStageCount">
/// ⚠ Stages authorising <b>no</b> service at all, counted apart from the rest. An empty whitelist is
/// not enforced, so such a stage is open to every service rather than closed to this hospital — the
/// blank means « personne n'a saisi la liste », and folding it into « non couvert » reports a refusal
/// that no data supports. Three stages of the catalogue are in this state today.
/// </param>
public sealed record HospitalStageCoverageResponse(
    int HospitalId,
    string HospitalName,
    int LevelId,
    string LevelLabel,
    int StageCount,
    int CoveredStageCount,
    int UnauthoredStageCount,
    IReadOnlyList<StageCoverageResponse> Stages);

/// <param name="ServicesAtHospital">
/// The authorised services this hospital holds for the stage — the ones a cell may actually be set
/// to, so the answer is « oui, et voici où », not merely « oui ».
/// </param>
public sealed record StageCoverageResponse(
    int StageId,
    string StageName,
    StageHospitalCoverage Coverage,
    int AllowedServiceCount,
    int ServicesAtHospitalCount,
    IReadOnlyList<CoverageServiceResponse> ServicesAtHospital);

public sealed record CoverageServiceResponse(int ServiceId, string Name);

internal sealed class GetHospitalStageCoverageQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetHospitalStageCoverageQuery, HospitalStageCoverageResponse>
{
    public async Task<Result<HospitalStageCoverageResponse>> Handle(
        GetHospitalStageCoverageQuery request, CancellationToken cancellationToken)
    {
        var hospital = await dbContext.Hospitals
            .AsNoTracking()
            .Where(h => h.Id == request.HospitalId)
            .Select(h => new { h.Id, h.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (hospital is null)
            return Result.Failure<HospitalStageCoverageResponse>(Error.NotFound(
                "Hospitals.NotFound", $"L'hôpital « {request.HospitalId} » est introuvable."));

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id, l.Label })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<HospitalStageCoverageResponse>(
                LevelErrors.NotFound(request.LevelId));

        var stages = await StagesQuery(dbContext, request.LevelId, request.HospitalId)
            .ToListAsync(cancellationToken);

        var services = (await ServicesAtHospitalQuery(dbContext, request.LevelId)
                .ToListAsync(cancellationToken))
            .ToDictionary(
                s => s.Id,
                s => (IReadOnlyList<CoverageServiceResponse>)s.AllowedServices
                    .Where(v => v.HospitalId == request.HospitalId)
                    .Select(v => new CoverageServiceResponse(v.Id, v.Name))
                    .OrderBy(v => v.Name, StringComparer.CurrentCulture)
                    .ToList());

        var rows = stages
            .Select(s => new StageCoverageResponse(
                s.StageId,
                s.StageName,
                StageHospitalCoverageTest.Of(s.AllowedServiceCount, s.ServicesAtHospitalCount),
                s.AllowedServiceCount,
                s.ServicesAtHospitalCount,
                services.GetValueOrDefault(s.StageId, [])))
            .ToList();

        return new HospitalStageCoverageResponse(
            hospital.Id,
            hospital.Name,
            level.Id,
            level.Label ?? $"niveau {level.Id}",
            rows.Count,
            rows.Count(r => r.Coverage == StageHospitalCoverage.Covered),
            rows.Count(r => r.Coverage == StageHospitalCoverage.NoServicesAuthored),
            rows);
    }

    /// <summary>
    /// One row per stage of the promotion, with the two counts the verdict is read from.
    /// </summary>
    /// <remarks>
    /// Both are correlated aggregates over a collection navigation, which translate; a projected
    /// collection of the services themselves would not. Deliberately unpaged — a level holds a
    /// handful of stages, so the answer is bounded by the catalogue rather than by the data.
    /// </remarks>
    internal static IQueryable<StageCoverageRow> StagesQuery(
        IApplicationDbContext dbContext, int levelId, int hospitalId) =>
        dbContext.Stages
            .AsNoTracking()
            .Where(s => s.LevelId == levelId)
            .OrderBy(s => s.Name)
            .Select(s => new StageCoverageRow(
                s.Id,
                s.Name,
                s.AllowedServices.Count,
                s.AllowedServices.Count(v => v.HospitalId == hospitalId)));

    /// <summary>
    /// The promotion's stages with their whole allowed-services list loaded, so the ones at the
    /// hospital can be <b>named</b>. Bounded by the catalogue — a level holds a handful of stages and
    /// the longest list in the base is 18 services.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Only the names come from here; the verdict never does.</b> An un-Included
    /// collection is indistinguishable from an empty one, and the in-memory provider hides the
    /// mistake by fixing navigations up from the change tracker — the trap <c>CnpnSpanFloor</c> is
    /// built around. Counted here, a forgotten <c>Include</c> would report every stage as
    /// <see cref="StageHospitalCoverage.NoServicesAuthored"/> on PostgreSQL with the whole suite
    /// green. Read from <see cref="StagesQuery"/>'s aggregates instead, it degrades to « couvert,
    /// mais aucun service nommé » — wrong in a way somebody can see.</para>
    /// <para>The list is filtered in memory rather than in the <c>Include</c>: the many-to-many is a
    /// skip navigation, and <c>SelectMany</c> over one is <c>NotImplementedException</c> on the
    /// in-memory provider — so a query written that way would compile, translate on Npgsql, and be
    /// untestable here.</para>
    /// </remarks>
    internal static IQueryable<Stage> ServicesAtHospitalQuery(
        IApplicationDbContext dbContext, int levelId) =>
        dbContext.Stages
            .AsNoTracking()
            .Where(s => s.LevelId == levelId)
            .Include(s => s.AllowedServices);

    internal sealed record StageCoverageRow(
        int StageId, string StageName, int AllowedServiceCount, int ServicesAtHospitalCount);
}
