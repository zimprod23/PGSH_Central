using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Delete;

/// <summary>
/// Supprime un service, une fois que plus rien ne le nomme.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Une suppression sans garde était destructrice de deux façons, et aucune ne
/// s'annonçait.</b> Le handler ne gardait rien — il portait un commentaire « (e.g., Check if
/// students are currently assigned to this service) » à la place de la garde. Mesuré contre le
/// schéma le 11/09/2026 :</para>
///
/// <list type="bullet">
///   <item><c>CohortSlotAssignments.ServiceId</c> et <c>ServicePeriods.ServiceId</c> sont
///   <c>RESTRICT</c> — la suppression remontait donc en violation de clé étrangère brute, c'est-à-dire
///   un <b>500</b> dont le seul contenu était le nom d'une contrainte PostgreSQL. Sur cette base, les
///   105 000 périodes reprises de l'Access rendent ce cas ordinaire : presque tout service réel est
///   retenu par sa propre histoire, et l'écran ne disait pas que le bouton ne marcherait jamais.</item>
///   <item><c>ServiceLevelCapacities</c>, <c>ServiceChefAssignment</c> et le rattachement du personnel
///   sont <b>CASCADE</b> — un service libre partait donc avec ses quotas, l'historique de ses chefs et
///   ses affectations d'employés, silencieusement.</item>
/// </list>
///
/// <para><b>Pourquoi les stages qui l'autorisent refusent au lieu de cascader.</b>
/// <c>StageAllowedServices</c> est en <c>CASCADE</c> lui aussi, mais ce n'est pas la même affaire
/// qu'un quota : la ligne joint <i>deux</i> entités indépendantes, et c'est le <b>stage</b> qui
/// survit amputé. Elle porte un <c>Rank</c> et un <c>PlacementMode</c> — deux décisions humaines — et
/// <c>ServiceRotationOrder</c> tient les rangs pour contigus depuis 1 : une ligne retirée par la base
/// laisse un trou, donc un numéro affiché à côté d'un service qui n'est plus la place qu'il occupe
/// dans la file. Le retrait passe par <c>ServiceRankWriter</c>, qui rebase, et c'est là-bas que
/// l'opérateur est renvoyé.</para>
/// </remarks>
internal sealed class DeleteServiceCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<DeleteServiceCommand>
{
    /// <summary>Au-delà, la phrase de refus cesse d'être lisible et le compte suffit.</summary>
    private const int MaxNamedStages = 3;

    public async Task<Result> Handle(DeleteServiceCommand request, CancellationToken cancellationToken)
    {
        // Les collections cascadées sont chargées ici plutôt que comptées après coup : une collection
        // non-Include ne se distingue pas d'une collection vide, et après la suppression il n'y a plus
        // rien à compter.
        var service = await dbContext.Services
            .Include(s => s.LevelCapacities)
            .Include(s => s.ChefHistory)
            .Include(s => s.Staff)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (service is null)
            return Result.Failure(ServiceErrors.NotFound(request.Id));

        var (holdings, holdsHistory) = await DescribeHoldingsAsync(request.Id, cancellationToken);
        if (holdings.Count > 0)
            return Result.Failure(ServiceErrors.StillInUse(service.Name, holdings, holdsHistory));

        auditTrail.RecordOutcome(
            ("serviceName", service.Name),
            ("hospitalId", service.HospitalId),
            ("quotasRemoved", service.LevelCapacities.Count),
            ("chefTenuresRemoved", service.ChefHistory.Count),
            ("staffDetached", service.Staff.Count));

        dbContext.Services.Remove(service);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Toutes les raisons pour lesquelles le service ne peut pas partir, dans les mots du refus — et
    /// si l'une d'elles est de l'histoire, ce qui change le conseil donné.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Comptées ensemble, jamais court-circuitées à la première.</b> Voir
    /// <c>ServiceErrors.StillInUse</c>.
    /// </remarks>
    private async Task<(List<string> Holdings, bool HoldsHistory)> DescribeHoldingsAsync(
        int serviceId, CancellationToken ct)
    {
        var holdings = new List<string>();

        int cells = await dbContext.CohortSlotAssignments
            .CountAsync(a => a.ServiceId == serviceId, ct);
        if (cells > 0) holdings.Add($"{cells} cellule(s) de planning");

        int periods = await dbContext.ServicePeriods
            .CountAsync(p => p.ServiceId == serviceId, ct);
        if (periods > 0) holdings.Add($"{periods} période(s) de stage déjà enregistrée(s)");

        var stages = await DescribeAuthorisingStagesAsync(serviceId, ct);
        if (stages is not null) holdings.Add(stages);

        return (holdings, periods > 0);
    }

    /// <summary>
    /// Les stages qui autorisent le service, nommés — « 2 stages » laisserait l'opérateur chercher
    /// lesquels, et lui dire où aller est toute la raison d'être du refus. Deux lectures plates
    /// plutôt qu'un saut de navigation : la jointure n'apporte rien et la traduction est acquise.
    /// </summary>
    private async Task<string?> DescribeAuthorisingStagesAsync(int serviceId, CancellationToken ct)
    {
        var stageIds = await dbContext.StageAllowedServices
            .Where(a => a.ServiceId == serviceId)
            .Select(a => a.StageId)
            .ToListAsync(ct);

        if (stageIds.Count == 0)
            return null;

        var names = await dbContext.Stages
            .Where(s => stageIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync(ct);

        string listed = string.Join(", ", names.Take(MaxNamedStages));
        int rest = names.Count - Math.Min(names.Count, MaxNamedStages);

        return rest > 0
            ? $"la liste des services autorisés de {listed} et {rest} autre(s) stage(s)"
            : $"la liste des services autorisés de {listed}";
    }
}
