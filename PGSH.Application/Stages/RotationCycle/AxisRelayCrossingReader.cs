using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Hospitals.Services.Occupancy;

namespace PGSH.Application.Stages.RotationCycle;

/// <param name="OtherPromotions">
/// Les promotions <b>autres</b> que celle recalculée qui se trouvent dans le service au moment du
/// pic. ⚠ C'est l'information que rien d'autre ne donne : la charge d'un service se lit déjà page
/// par page, mais « qui d'autre est là quand j'aurai poussé » ne se lit nulle part.
/// </param>
internal sealed record ServiceCrossing(
    int ServiceId,
    string ServiceName,
    string HospitalName,
    int PeakBefore,
    int PeakAfter,
    DateOnly PeakStart,
    DateOnly PeakEnd,
    IReadOnlyList<string> OtherPromotions)
{
    public int Increase => PeakAfter - PeakBefore;
}

/// <param name="Listed">
/// Les pires, par ampleur de hausse. ⚠ <b>Borné, et le total est à côté</b> : une réponse à objet
/// unique cache une collection non paginée à tout <c>grep</c> de <c>List&lt;T&gt;</c>, et c'est ce
/// qui a mis 4 725 étudiants dans un seul objet.
/// </param>
internal sealed record AxisRelayCrossings(
    int ServicesExamined,
    int ServicesWherePeakRises,
    IReadOnlyList<ServiceCrossing> Listed);

/// <summary>
/// « Une fois l'axe poussé, où mes étudiants tombent-ils sur une autre promotion ? »
/// </summary>
/// <remarks>
/// <para><b>La question que rien ne posait.</b> Le recalcul ne change pas <em>quel</em> service une
/// cohorte occupe — seulement <em>quand</em>. Mais décaler 3 700 rotations de quelques semaines les
/// fait arriver là où une autre promotion est déjà debout, et aucune lecture existante ne regarde ce
/// croisement : la page d'un service montre sa charge telle qu'elle est, la grille montre un plan à
/// la fois.</para>
///
/// <para>⚠ <b>C'est un rapport, jamais une garde</b> — décision du 12/09/2026 : cette faculté
/// dépasse la capacité de ses services dans la plupart des cas, et c'est son fonctionnement, pas un
/// défaut. Rien ici ne refuse quoi que ce soit ; l'acte se joue et le chiffre se voit.</para>
///
/// <para>⚠ <b>Le pic, jamais la somme.</b> Deux créneaux qui touchent la même fenêtre sans se
/// toucher l'un l'autre ne s'additionnent pas — c'est le défaut mesuré le 03/09/2026, qui affichait
/// 118 sur un service qui n'a jamais porté plus de 62. <see cref="OccupancyTimeline"/> découpe
/// l'année aux frontières des fenêtres et rend une charge simultanée exacte par segment ; ce
/// lecteur ne refait pas cette arithmétique, il lui donne des dates différentes.</para>
///
/// <para>⚠ <b>Deux passages, un seul jeu de placements.</b> Le « avant » et le « après » se
/// calculent sur les mêmes lignes, en substituant la fenêtre proposée aux seules cellules des
/// colonnes déplacées. Charger deux fois laisserait les deux moitiés diverger si quelqu'un écrivait
/// entre les deux.</para>
/// </remarks>
internal sealed class AxisRelayCrossingReader(IApplicationDbContext dbContext)
{
    /// <summary>Combien de services au plus sont détaillés dans la réponse.</summary>
    private const int MaxListed = 20;

    public async Task<AxisRelayCrossings> ReadAsync(
        int levelId,
        IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> movedColumns,
        CancellationToken ct)
    {
        if (movedColumns.Count == 0)
            return new AxisRelayCrossings(0, 0, []);

        var numbers = movedColumns.Keys.ToList();

        var services = await AffectedServicesQuery(dbContext, levelId, numbers)
            .AsNoTracking().ToListAsync(ct);

        if (services.Count == 0)
            return new AxisRelayCrossings(0, 0, []);

        var serviceIds = services.Select(s => s.ServiceId).Distinct().ToList();

        // La fenêtre la plus large que l'opération puisse toucher : l'ancienne et la nouvelle
        // position des colonnes déplacées réunies. Plus étroit raterait un croisement au bord.
        var from = movedColumns.Values.Min(w => w.Start);
        var to = movedColumns.Values.Max(w => w.End);

        var placements = await PlacementsQuery(dbContext, serviceIds, from.AddYears(-1), to.AddYears(1))
            .AsNoTracking().ToListAsync(ct);

        var crossings = new List<ServiceCrossing>(serviceIds.Count);

        foreach (var group in placements.GroupBy(p => p.ServiceId))
        {
            var before = group.Select(p => p.Placement).ToList();

            var after = group
                .Select(p => p.LevelId == levelId && movedColumns.TryGetValue(p.PeriodNumber, out var w)
                    ? p.Placement with { StartDate = w.Start, EndDate = w.End }
                    : p.Placement)
                .ToList();

            var peakBefore = Peak(before);
            var peakAfter = Peak(after);

            if (peakAfter is null || peakBefore is null)
                continue;

            int loadBefore = peakBefore.Occupants.Sum(o => o.Students);
            int loadAfter = peakAfter.Occupants.Sum(o => o.Students);

            if (loadAfter <= loadBefore)
                continue;

            var service = services.First(s => s.ServiceId == group.Key);

            crossings.Add(new ServiceCrossing(
                group.Key,
                service.ServiceName,
                service.HospitalName,
                loadBefore,
                loadAfter,
                peakAfter.StartDate,
                peakAfter.EndDate,
                peakAfter.Occupants
                    .Where(o => o.LevelId != levelId)
                    .Select(o => o.LevelLabel)
                    .Distinct()
                    .Order()
                    .ToList()));
        }

        return new AxisRelayCrossings(
            serviceIds.Count,
            crossings.Count,
            crossings.OrderByDescending(c => c.Increase).Take(MaxListed).ToList());
    }

    /// <summary>
    /// Le segment où le service porte le plus de monde. ⚠ Par <see cref="OccupancyTimeline"/>, qui
    /// découpe aux frontières : additionner les fenêtres qui touchent une période compterait deux
    /// fois deux créneaux consécutifs.
    /// </summary>
    private static OccupancySegment? Peak(IReadOnlyList<OccupancyPlacement> placements) =>
        OccupancyTimeline.Build(placements)
            .OrderByDescending(s => s.Occupants.Sum(o => o.Students))
            .FirstOrDefault();

    /// <summary>
    /// Les services que les colonnes déplacées de cette promotion occupent.
    /// </summary>
    internal static IQueryable<AffectedService> AffectedServicesQuery(
        IApplicationDbContext dbContext, int levelId, IReadOnlyList<int> periodNumbers) =>
        dbContext.CohortSlotAssignments
            .Where(a => a.Cohort.Stage.LevelId == levelId
                     && periodNumbers.Contains(a.StageSlot.PeriodNumber))
            .Select(a => new AffectedService(
                a.ServiceId,
                a.Service.Name,
                a.Service.Hospital.Name))
            .Distinct();

    /// <summary>
    /// Toute occupation de ces services sur l'étendue — <b>toutes promotions confondues</b>, puisque
    /// la question porte exactement sur les autres.
    /// </summary>
    /// <remarks>
    /// ⚠ Plate et au premier niveau, pliée en mémoire ensuite. <c>Assignments.Count(...)</c> est un
    /// agrégat scalaire et non une collection projetée — c'est cette dernière que Npgsql refuse. Le
    /// compte est <b>exactement</b> celui de la page du service et de
    /// <c>ServiceOccupancyCalculator</c>, délocalisés exclus : une lecture qui compterait autrement
    /// expliquerait un pic par un nombre que personne d'autre ne produit.
    /// </remarks>
    internal static IQueryable<PlacementRow> PlacementsQuery(
        IApplicationDbContext dbContext, IReadOnlyList<int> serviceIds, DateOnly from, DateOnly to) =>
        dbContext.CohortSlotAssignments
            .Where(a => serviceIds.Contains(a.ServiceId)
                     && a.StageSlot.StartDate <= to
                     && from <= a.StageSlot.EndDate)
            .Select(a => new PlacementRow(
                a.ServiceId,
                a.Cohort.Stage.LevelId,
                a.StageSlot.PeriodNumber,
                new OccupancyPlacement(
                    a.Cohort.StageId,
                    a.Cohort.Stage.Name,
                    a.Cohort.Stage.LevelId,
                    a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                    a.StageSlot.PeriodNumber,
                    a.CohortId,
                    a.Cohort.AcademicGroup.GroupNumber,
                    a.Cohort.Assignments.Count(x => !x.ServicePeriods.Any(p => p.IsDelocalized)),
                    a.StageSlot.StartDate,
                    a.StageSlot.EndDate)));

    internal sealed record AffectedService(int ServiceId, string ServiceName, string HospitalName);

    internal sealed record PlacementRow(
        int ServiceId, int LevelId, int PeriodNumber, OccupancyPlacement Placement);
}
