using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Audit;
using PGSH.SharedKernel;

namespace PGSH.Application.Audit;

internal sealed class GetAuditLogQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetAuditLogQuery, AuditLogPage>
{
    /// <summary>
    /// Combien de codes d'actes distincts la réponse énumère. Le journal en contient aujourd'hui une
    /// dizaine et le code est borné par le nombre de commandes auditées, donc le plafond ne mord pas
    /// — il est là pour que la liste reste bornée par construction et non par la donnée.
    /// </summary>
    private const int MaxReportedActions = 100;

    public async Task<Result<AuditLogPage>> Handle(
        GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        // Le journal dit qui a agi sur le dossier de la faculté entière : c'est une lecture
        // d'administration, pas une lecture de service. Même porte que la déliberation ou la
        // suppression d'une année.
        var access = authorizer.EnsureIsAdministrative(AuditErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<AuditLogPage>(access.Error);

        var scope = ScopedQuery(
            dbContext, request.Action, request.EntityType, request.EntityId, request.From, request.To);

        var page = await scope
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToPaginatedResponseAsync(
                request.EffectivePageNumber,
                request.EffectivePageSize,
                a => new AuditLogRow(
                    a.Id, a.Action, a.EntityType, a.EntityId, a.Metadata, a.CreatedAt,
                    a.PerformedByUserId),
                cancellationToken);

        var actors = await ResolveActorsAsync(page.Items, cancellationToken);

        var entries = page.Items
            .Select(row => new AuditLogEntryResponse(
                row.Id, row.Action, row.EntityType, row.EntityId, row.Metadata, row.CreatedAt,
                row.PerformedByUserId,
                row.PerformedByUserId is { } id ? actors.GetValueOrDefault(id) : null))
            .ToList();

        var counted = await ActionCountsQuery(dbContext).ToListAsync(cancellationToken);

        // Le total est pris sur l'ensemble compté, avant le plafond : « 5 sur 5 » et « 5 sur 300 »
        // ne disent pas la même chose, et c'est la seconde qui compte quand la liste est tronquée.
        int totalEntries = counted.Sum(a => a.Count);

        var actions = counted
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Action, StringComparer.Ordinal)
            .Take(MaxReportedActions)
            .ToList();

        return new AuditLogPage(
            new PaginatedResponse<AuditLogEntryResponse>(
                entries, page.PageNumber, page.PageSize, page.TotalCount),
            actions,
            totalEntries);
    }

    /// <summary>
    /// Les noms des auteurs de <b>cette page</b>, et rien d'autre.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Une seconde lecture plate, parce que la jointure traverse deux types.</b>
    /// <c>AuditLog.PerformedByUserId</c> est un <c>Guid</c> (le <c>sub</c> Keycloak) tandis que
    /// <c>User.IdentityProviderId</c> est une <c>string</c> — il n'y a pas de clé étrangère entre les
    /// deux, et une jointure demanderait à Npgsql de traduire un <c>Guid.ToString()</c> dans un
    /// prédicat. Les ids sont donc convertis <i>en mémoire</i>, puis remis dans un <c>IN</c>.
    /// C'est aussi ce qui rend le nom facultatif sans effort : un auteur introuvable ne fait pas
    /// disparaître son entrée du journal.
    /// </remarks>
    private async Task<Dictionary<Guid, string>> ResolveActorsAsync(
        IReadOnlyCollection<AuditLogRow> rows, CancellationToken ct)
    {
        var ids = rows
            .Select(r => r.PerformedByUserId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return [];

        var keys = ids.Select(id => id.ToString()).ToList();

        var users = await ActorsQuery(dbContext, keys).ToListAsync(ct);

        return users
            .Where(u => Guid.TryParse(u.IdentityProviderId, out _))
            .ToDictionary(
                u => Guid.Parse(u.IdentityProviderId!),
                u => string.IsNullOrWhiteSpace($"{u.FirstName}{u.LastName}")
                    ? u.Email
                    : $"{u.FirstName} {u.LastName}".Trim());
    }

    /// <summary>
    /// Le journal, filtré. Nommée pour que <c>SqlTranslationTests</c> puisse la compiler.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Les bornes sont des instants UTC, jamais des jours</b> — <paramref name="from"/> inclus,
    /// <paramref name="to"/> exclu. Résolues ici à partir de <c>DateOnly</c>, elles auraient pris
    /// minuit <i>UTC</i> alors que l'écran affiche l'heure du navigateur : une entrée du 02/09 à
    /// 22:16 UTC, lue « 03/09 00:16 » à Casablanca, disparaissait d'un filtre « du 3 au 3 ». La
    /// journée est une notion du calendrier de celui qui lit, donc c'est le client qui la traduit —
    /// et le serveur ne suppose aucun fuseau, ce qu'il ne saurait pas faire (le Maroc bascule à
    /// UTC+0 pendant le ramadan).
    /// </remarks>
    internal static IQueryable<AuditLog> ScopedQuery(
        IApplicationDbContext dbContext,
        string? action,
        string? entityType,
        string? entityId,
        DateTime? from,
        DateTime? to)
    {
        var query = dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(entityId))
            query = query.Where(a => a.EntityId == entityId);

        if (from is { } start)
        {
            var startUtc = AsUtc(start);
            query = query.Where(a => a.CreatedAt >= startUtc);
        }

        if (to is { } end)
        {
            var endUtc = AsUtc(end);
            query = query.Where(a => a.CreatedAt < endUtc);
        }

        return query;
    }

    /// <summary>
    /// L'instant, en UTC, quel que soit le <see cref="DateTimeKind"/> que la liaison a produit.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Les trois cas se traitent différemment et se confondre décale la borne d'heures.</b> La
    /// liaison depuis la query string donne <c>Utc</c> pour un <c>…Z</c>, <c>Local</c> pour un
    /// décalage explicite, et <c>Unspecified</c> pour une date nue. Un <c>SpecifyKind(Utc)</c>
    /// uniforme prendrait une heure locale pour de l'UTC ; un <c>ToUniversalTime()</c> uniforme
    /// décalerait une valeur <c>Unspecified</c> du fuseau du serveur. Npgsql refuse par ailleurs un
    /// <c>timestamptz</c> qui n'est pas explicitement UTC, donc la question ne peut pas être éludée.
    /// C'est la même famille que le défaut que ces bornes viennent de corriger : une date juste
    /// comparée dans le mauvais repère.
    /// </remarks>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Chaque code d'acte présent dans le journal, avec son effectif — les puces de filtrage.
    /// </summary>
    /// <remarks>
    /// <para>Compté où sont les lignes, jamais dérivé de la page : un décompte lu sur cinquante
    /// entrées dirait « PARTITIONS_ASSIGNED : 3 » d'un journal qui en contient trois cents.</para>
    /// <para>⚠ <b>L'agrégat est en SQL, le tri ne l'est pas — et c'est le fournisseur in-memory qui
    /// impose la coupure</b>, pas Npgsql. Un <c>OrderByDescending</c> sur une propriété <i>projetée</i>
    /// depuis un <c>GroupBy</c> est refusé en mémoire (« could not be translated ») alors que
    /// PostgreSQL le traduit sans broncher — le miroir de l'angle mort habituel, déjà rencontré sur
    /// <c>SelectMany</c> au-dessus d'une skip navigation. Le <c>Count()</c> reste donc côté base, où
    /// il doit être ; le classement se fait sur la liste rendue, bornée par le nombre de commandes
    /// auditées (une quarantaine), pas par la donnée.</para>
    /// </remarks>
    internal static IQueryable<AuditActionCount> ActionCountsQuery(IApplicationDbContext dbContext) =>
        dbContext.AuditLogs
            .AsNoTracking()
            .GroupBy(a => a.Action)
            .Select(g => new AuditActionCount(g.Key, g.Count()));

    /// <summary>Les utilisateurs dont le <c>sub</c> Keycloak figure dans <paramref name="identityIds"/>.</summary>
    internal static IQueryable<ActorRow> ActorsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<string> identityIds) =>
        dbContext.Users
            .AsNoTracking()
            .Where(u => u.IdentityProviderId != null && identityIds.Contains(u.IdentityProviderId))
            .Select(u => new ActorRow(u.IdentityProviderId, u.FirstName, u.LastName, u.Email));

    internal sealed record AuditLogRow(
        Guid Id, string Action, string EntityType, string? EntityId, string? Metadata,
        DateTime CreatedAt, Guid? PerformedByUserId);

    internal sealed record ActorRow(
        string? IdentityProviderId, string FirstName, string LastName, string Email);
}
