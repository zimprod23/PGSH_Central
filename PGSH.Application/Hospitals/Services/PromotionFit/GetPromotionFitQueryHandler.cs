using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.PromotionFit;

/// <summary>
/// Builds the fit panel from <b>five flat reads</b> and the pure arithmetic of
/// <see cref="PromotionAxis"/>.
///
/// <para>⚠ <b>The service pool is the arranger's, rule for rule.</b> External out, non-admitting out,
/// <c>Reserved</c> out, capacity read through <see cref="Service.CapacityFor"/> — the same four
/// clauses <c>RotationArranger</c> applies when it builds its queue. A panel that counted places the
/// arranger will not use would promise room that no arrange can reach, which is worse than no panel:
/// it would be believed.</para>
///
/// <para>⚠ <b>The promotions are read whole even when one is asked about.</b> « Aussi autorisé par »
/// is a fact about a service, and a service is shared: computing it inside the filter would tell the
/// 4ᵉ MED that its services are hers alone. Same rule as the occupancy report — a filter picks what
/// is <i>listed</i>, never what is <i>counted</i>.</para>
///
/// <para>⚠ <b>No collection subquery in any projection.</b> The holds are aggregated in a
/// <c>Where</c> (a predicate, which translates) and never in a <c>Select</c>; the quotas ride on an
/// <c>Include</c>. That is the shape Npgsql refuses and the in-memory provider cannot see —
/// <c>SqlTranslationTests</c> compiles all five reads.</para>
/// </summary>
internal sealed class GetPromotionFitQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetPromotionFitQuery, PromotionFitResponse>
{
    public async Task<Result<PromotionFitResponse>> Handle(
        GetPromotionFitQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PromotionFitResponse>(year.Error);

        int yearId = year.Value;

        string yearLabel = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == yearId)
            .Select(y => y.Label)
            .FirstAsync(cancellationToken);

        if (request.LevelId is { } asked)
        {
            var level = await dbContext.Levels
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == asked, cancellationToken);

            if (level is null)
                return Result.Failure<PromotionFitResponse>(LevelErrors.NotFound(asked));

            // Refused rather than reported empty: « Retrait » has no stage and no cohort, so an empty
            // panel for it would read as a promotion with nothing configured.
            if (!level.IsPromotion)
                return Result.Failure<PromotionFitResponse>(
                    LevelErrors.NotAPromotion(level.Label ?? $"niveau {asked}"));
        }

        var promotions = await PromotionsQuery(dbContext, yearId).ToListAsync(cancellationToken);

        if (promotions.Count == 0)
            return Result.Failure<PromotionFitResponse>(
                PromotionFitErrors.NoPromotionsInYear(yearLabel));

        var levelIds = promotions.Select(p => p.LevelId).ToList();

        var plannable = await PlannableHeadcountQuery(dbContext, yearId)
            .ToDictionaryAsync(h => h.LevelId, h => h.Students, cancellationToken);

        var held = await HeldHeadcountQuery(dbContext, yearId)
            .ToDictionaryAsync(h => h.LevelId, h => h.Students, cancellationToken);

        var stages = await StagesQuery(dbContext, levelIds).ToListAsync(cancellationToken);
        var stageIds = stages.Select(s => s.StageId).ToList();

        var authorisations = await AuthorisationsQuery(dbContext, stageIds).ToListAsync(cancellationToken);
        var serviceIds = authorisations.Select(a => a.ServiceId).Distinct().ToList();

        var services = (await ServicesQuery(dbContext, serviceIds).ToListAsync(cancellationToken))
            .ToDictionary(s => s.Id);

        var stagesByLevel = stages.ToLookup(s => s.LevelId);
        var authorisationsByStage = authorisations.ToLookup(a => a.StageId);

        // Every promotion that authorises a service, computed before any filter — see the class note.
        var claimants = Claimants(promotions, stages, authorisations);

        var rows = promotions
            .Select(promotion => BuildPromotionRow(
                promotion,
                plannable.GetValueOrDefault(promotion.LevelId),
                held.GetValueOrDefault(promotion.LevelId),
                stagesByLevel[promotion.LevelId].ToList(),
                authorisationsByStage,
                services,
                claimants))
            .ToList();

        var listed = rows
            .Where(r => request.LevelId is not { } only || r.LevelId == only)
            .OrderBy(r => r.AcademicProgram)
            .ThenBy(r => r.LevelLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PromotionFitResponse(
            yearId,
            yearLabel,
            Scope(listed, request, yearLabel),
            Totals(listed),
            listed,
            Notes(listed, services));
    }

    // ── Per promotion ──────────────────────────────────────────────────────────────────────────

    private static PromotionFitRow BuildPromotionRow(
        PromotionLevel promotion,
        int students,
        int heldStudents,
        List<FitStage> stages,
        ILookup<int, StageAuthorisation> authorisationsByStage,
        Dictionary<int, Service> services,
        Dictionary<int, List<string>> claimants)
    {
        var axis = PromotionAxis.Lay(stages.ConvertAll(s => (s.StageId, s.DurationInDays)));
        var periods = axis.Weights.ToDictionary(w => w.StageId, w => w.Periods);

        var stageRows = stages
            .Select(stage => BuildStageRow(
                stage,
                periods.GetValueOrDefault(stage.StageId),
                axis.Timeline,
                students,
                promotion,
                authorisationsByStage[stage.StageId].ToList(),
                services,
                claimants))
            .OrderBy(s => s.Margin)
            .ThenBy(s => s.StageName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int shortfall = stageRows.Count == 0 ? 0 : Math.Max(0, -stageRows.Min(s => s.Margin));

        return new PromotionFitRow(
            promotion.LevelId,
            promotion.LevelLabel,
            promotion.AcademicProgram,
            students,
            heldStudents,
            axis.Timeline,
            axis.ColumnDays,
            stages.Sum(s => Math.Max(0, s.DurationInDays)),
            StateOf(students, stageRows),
            shortfall,
            stageRows);
    }

    /// <summary>
    /// ⚠ Ordered worst-first, and « impossible » outranks « en dépassement » however deep the
    /// overflow: one is a catalogue gap the arrange refuses on, the other a decision the faculty may
    /// take. A promotion carrying both is described by the one that stops it.
    /// </summary>
    private static PromotionFitState StateOf(int students, List<PromotionFitStageRow> stages)
    {
        if (stages.Count == 0) return PromotionFitState.NoStages;
        if (stages.Exists(Unplaceable)) return PromotionFitState.Unplaceable;

        // Asked after the catalogue states, deliberately: a promotion with nobody registered *and* a
        // stage authorising no service still owes that configuration before anybody arrives.
        if (students == 0) return PromotionFitState.NoStudents;

        return stages.Exists(s => s.State == StageFitState.OverCapacity)
            ? PromotionFitState.OverCapacity
            : PromotionFitState.Fits;
    }

    private static bool Unplaceable(PromotionFitStageRow stage) =>
        stage.State is StageFitState.NoAllowedServices
                    or StageFitState.NoServiceAdmits
                    or StageFitState.AllServicesReserved
                    or StageFitState.NoDuration;

    // ── Per stage ──────────────────────────────────────────────────────────────────────────────

    private static PromotionFitStageRow BuildStageRow(
        FitStage stage,
        int periods,
        int timeline,
        int students,
        PromotionLevel promotion,
        List<StageAuthorisation> authorised,
        Dictionary<int, Service> services,
        Dictionary<int, List<string>> claimants)
    {
        var serviceRows = authorised
            .Select(a => BuildServiceRow(a, promotion, services, claimants))
            .OrderByDescending(s => s.Places)
            .ThenBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The arranger's own four clauses, in its own order — see the class note.
        var usable = serviceRows
            .Where(s => !s.IsExternal && s.Admits && s.ParticipatesInRotation && s.Places > 0)
            .ToList();

        int places = usable.Sum(s => s.Places);
        int atOnce = PromotionAxis.SimultaneousStudents(students, periods, timeline);


        return new PromotionFitStageRow(
            stage.StageId,
            stage.StageName,
            stage.DurationInDays,
            periods,
            atOnce,
            places,
            places - atOnce,
            StateOf(stage, serviceRows, usable, places, atOnce),
            serviceRows.Count,
            usable.Count,
            serviceRows.Count(s => !s.ParticipatesInRotation),
            serviceRows.Count(s => !s.Admits),
            serviceRows.Count(s => s.IsExternal),
            serviceRows);
    }

    /// <summary>
    /// ⚠ <b>Four ways to be unplaceable, named apart.</b> « aucun service autorisé », « aucun n'admet
    /// cette promotion » and « tous réservés » are three different acts — author the list, grant a
    /// quota, release a reservation — and the arranger itself refuses with three different errors.
    /// Collapsing them into « 0 place » would print the same cell for all three and for a stage whose
    /// services simply hold too few.
    /// </summary>
    private static StageFitState StateOf(
        FitStage stage,
        List<PromotionFitServiceRow> authorised,
        List<PromotionFitServiceRow> usable,
        int places,
        int atOnce)
    {
        if (stage.DurationInDays <= 0) return StageFitState.NoDuration;
        if (authorised.Count == 0) return StageFitState.NoAllowedServices;

        if (usable.Count == 0)
        {
            return authorised.TrueForAll(s => s.Admits && !s.IsExternal && !s.ParticipatesInRotation)
                ? StageFitState.AllServicesReserved
                : StageFitState.NoServiceAdmits;
        }

        return places < atOnce ? StageFitState.OverCapacity : StageFitState.Fits;
    }

    private static PromotionFitServiceRow BuildServiceRow(
        StageAuthorisation authorisation,
        PromotionLevel promotion,
        Dictionary<int, Service> services,
        Dictionary<int, List<string>> claimants)
    {
        int levelId = promotion.LevelId;

        // A service the join names but the read did not return cannot happen; if it ever did, the
        // safe answer is the one that shows no places rather than the one that invents them.
        if (!services.TryGetValue(authorisation.ServiceId, out var service))
        {
            return new PromotionFitServiceRow(
                authorisation.ServiceId, $"#{authorisation.ServiceId}", "", 0,
                authorisation.ParticipatesInRotation, false, false, false, []);
        }

        return new PromotionFitServiceRow(
            service.Id,
            service.Name,
            service.Hospital?.Name ?? "",
            // Delegated, never re-derived: « aucun quota » means open at the service's own capacity,
            // and a quota replaces that capacity rather than sitting under it.
            service.CapacityFor(levelId),
            authorisation.ParticipatesInRotation,
            service.Admits(levelId),
            service.IsExternal,
            service.AllowsOverCapacity,
            // « Also »: the promotion being described is dropped from its own service's claimants.
            claimants.GetValueOrDefault(service.Id, [])
                .Where(label => label != promotion.LevelLabel)
                .ToList());
    }

    /// <summary>
    /// Which promotions authorise each service, over the whole year. The label of the promotion being
    /// described is filtered out by the caller, so the column reads « <em>also</em> ».
    /// </summary>
    private static Dictionary<int, List<string>> Claimants(
        List<PromotionLevel> promotions,
        List<FitStage> stages,
        List<StageAuthorisation> authorisations)
    {
        var levelOfStage = stages.ToDictionary(s => s.StageId, s => s.LevelId);
        var labelOfLevel = promotions.ToDictionary(p => p.LevelId, p => p.LevelLabel);

        return authorisations
            .Where(a => levelOfStage.ContainsKey(a.StageId))
            .GroupBy(a => a.ServiceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => labelOfLevel[levelOfStage[a.StageId]])
                      .Distinct()
                      .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                      .ToList());
    }

    // ── Totals and words ───────────────────────────────────────────────────────────────────────

    private static PromotionFitTotals Totals(List<PromotionFitRow> rows)
    {
        var worst = rows
            .SelectMany(r => r.Stages)
            .Where(s => s.Margin < 0)
            .MinBy(s => s.Margin);

        return new PromotionFitTotals(
            Promotions:              rows.Count,
            PromotionsThatFit:       rows.Count(r => r.State == PromotionFitState.Fits),
            PromotionsOverCapacity:  rows.Count(r => r.State == PromotionFitState.OverCapacity),
            PromotionsUnplaceable:   rows.Count(r => r.State == PromotionFitState.Unplaceable),
            PromotionsWithoutStages: rows.Count(r => r.State == PromotionFitState.NoStages),
            Students:                rows.Sum(r => r.Students),
            Stages:                  rows.Sum(r => r.Stages.Count),
            StagesOverCapacity:      rows.Sum(r => r.Stages.Count(s => s.State == StageFitState.OverCapacity)),
            StagesUnplaceable:       rows.Sum(r => r.Stages.Count(Unplaceable)),
            WorstShortfall:          worst is null ? 0 : -worst.Margin,
            WorstShortfallStage:     worst?.StageName);
    }

    private static string Scope(List<PromotionFitRow> rows, GetPromotionFitQuery request, string yearLabel)
    {
        string what = request.LevelId is not null && rows.Count == 1
            ? rows[0].LevelLabel
            : $"{rows.Count} promotion(s)";

        return $"{what} — {yearLabel}";
    }

    /// <summary>
    /// What the numbers rest on — said only when it is true of this data.
    /// </summary>
    /// <remarks>
    /// ⚠ Same rule as <c>OccupancyReport.Notes</c> and <c>ExportNotes</c>: a warning that fires
    /// whatever the data says is noise, and noise is dismissed — which puts the real one out of
    /// sight. Each of these names a state whose fix is a different act.
    /// </remarks>
    private static List<string> Notes(List<PromotionFitRow> rows, Dictionary<int, Service> services)
    {
        var notes = new List<string>();

        if (rows.Exists(r => r.State == PromotionFitState.Unplaceable))
        {
            int stages = rows.Sum(r => r.Stages.Count(Unplaceable));
            notes.Add(
                $"{stages} stage(s) n'ont aucun service que la rotation puisse tirer : la répartition "
                + "automatique les refusera, quel que soit l'effectif. C'est une liste de services à "
                + "saisir, pas un manque de places.");
        }

        if (rows.Exists(r => r.State == PromotionFitState.NoStages))
        {
            notes.Add(
                "Une promotion au moins n'a aucun stage au catalogue : elle n'a donc pas d'axe, et "
                + "rien de ce qui suit ne la concerne tant qu'un stage ne lui est pas rattaché.");
        }

        // ⚠ The single most load-bearing caveat of the whole panel: the arranger does not read live
        // occupancy, so two promotions can each be told they fit into the same places.
        var shared = services.Keys.Count == 0 ? 0 : SharedServiceCount(rows);

        if (shared > 0)
        {
            notes.Add(
                $"{shared} service(s) sont autorisés par plusieurs promotions à la fois. Les places "
                + "comptées ici ne sont retirées à personne : deux promotions peuvent chacune « tenir » "
                + "dans les mêmes lits si elles y passent en même temps. La répartition automatique ne "
                + "le verra pas non plus — elle pondère par la capacité et ne lit jamais l'occupation "
                + "réelle.");
        }

        if (rows.Exists(r => r.HeldStudents > 0))
        {
            int frozen = rows.Sum(r => r.HeldStudents);
            notes.Add(
                $"{frozen} inscription(s) sont signalées et gelées : elles ne sont pas comptées "
                + "ci-dessus, parce que la répartition les laissera de côté. Levez le signalement et "
                + "l'effectif à placer augmente d'autant.");
        }

        var authored = services.Values
            .Where(s => !s.HasLevelRestrictions)
            .Select(s => s.Capacity)
            .Distinct()
            .ToList();

        if (services.Count > 1 && authored.Count == 1)
        {
            notes.Add(
                $"Les services sans quota déclarent tous la même capacité ({authored[0]}) : c'est la "
                + "valeur par défaut de l'import. Les places comptées ici sont donc mesurées contre un "
                + "chiffre que personne n'a saisi.");
        }

        return notes;
    }

    private static int SharedServiceCount(List<PromotionFitRow> rows) =>
        rows.SelectMany(r => r.Stages)
            .SelectMany(s => s.Services)
            .Where(s => s.AlsoAuthorisedBy.Count > 0)
            .Select(s => s.ServiceId)
            .Distinct()
            .Count();

    // ── The reads ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The promotions of the year — every level somebody is registered in.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>Level.IsPromotion</c> is a computed property and a method call in a <c>Where</c> is
    /// refused by the provider, so the rule is written as <c>Year &gt; 0</c> here. Named and
    /// <c>internal static</c> so <c>SqlTranslationTests</c> can compile it — the in-memory provider
    /// would have evaluated the property client-side and told us nothing.
    /// </remarks>
    internal static IQueryable<PromotionLevel> PromotionsQuery(IApplicationDbContext dbContext, int yearId) =>
        dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Year > 0)
            .Where(l => dbContext.Registrations.Any(r => r.LevelId == l.Id && r.AcademicYearId == yearId))
            .OrderBy(l => l.AcademicProgram)
            .ThenBy(l => l.Year)
            .Select(l => new PromotionLevel(
                l.Id,
                l.Label ?? (l.Year + "e année " + l.AcademicProgram),
                l.AcademicProgram));

    /// <summary>
    /// Who the cut will actually pick up, per promotion.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The hold rule is <c>RegistrationHoldPolicy</c>'s, not a copy of it.</b> The panel counts
    /// the population planning may reach, so it must agree with the act that reaches it — and the
    /// aggregate lives in a <c>Where</c>, which is the one place a collection may be aggregated
    /// without meeting the shape Npgsql refuses.
    /// </remarks>
    internal static IQueryable<LevelHeadcount> PlannableHeadcountQuery(
        IApplicationDbContext dbContext, int yearId) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId)
            .Where(RegistrationHoldPolicy.Plannable)
            .GroupBy(r => r.LevelId)
            .Select(g => new LevelHeadcount(g.Key, g.Count()));

    /// <summary>The other half, so « 0 à placer » can say whether it is empty or frozen.</summary>
    internal static IQueryable<LevelHeadcount> HeldHeadcountQuery(
        IApplicationDbContext dbContext, int yearId) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId)
            .Where(RegistrationHoldPolicy.OnHold)
            .GroupBy(r => r.LevelId)
            .Select(g => new LevelHeadcount(g.Key, g.Count()));

    /// <summary>
    /// The catalogue side of the axis. ⚠ <c>Stage</c> is year-invariant, so this is deliberately not
    /// scoped by year — the durations of « Chirurgie » outlive every promotion.
    /// </summary>
    internal static IQueryable<FitStage> StagesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> levelIds) =>
        dbContext.Stages
            .AsNoTracking()
            .Where(s => levelIds.Contains(s.LevelId))
            .OrderBy(s => s.Name)
            .Select(s => new FitStage(s.Id, s.Name, s.LevelId, s.DurationInDays));

    /// <summary>Which services each stage authorises, and whether the rotation may draw them.</summary>
    internal static IQueryable<StageAuthorisation> AuthorisationsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> stageIds) =>
        dbContext.StageAllowedServices
            .AsNoTracking()
            .Where(a => stageIds.Contains(a.StageId))
            .Select(a => new StageAuthorisation(
                a.StageId,
                a.ServiceId,
                a.PlacementMode == ServicePlacementMode.Rotation));

    /// <summary>
    /// The services themselves, quotas included — entities rather than a projection, so
    /// <see cref="Service.Admits"/> and <see cref="Service.CapacityFor"/> answer instead of the rule
    /// being written a second time here.
    /// </summary>
    internal static IQueryable<Service> ServicesQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> serviceIds) =>
        dbContext.Services
            .AsNoTracking()
            .Include(s => s.Hospital)
            .Include(s => s.LevelCapacities)
            .Where(s => serviceIds.Contains(s.Id));
}

/// <summary>One promotion of the year, flat by construction.</summary>
internal sealed record PromotionLevel(int LevelId, string LevelLabel, AcademicProgram AcademicProgram);

internal sealed record LevelHeadcount(int LevelId, int Students);

internal sealed record FitStage(int StageId, string StageName, int LevelId, int DurationInDays);

internal sealed record StageAuthorisation(int StageId, int ServiceId, bool ParticipatesInRotation);
