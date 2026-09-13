using System.Data.Common;
using System.Net.Sockets;

namespace PGSH.API.Infrastructure;

/// <summary>
/// Cette exception dit-elle que la base est <b>injoignable</b>, ou qu'une requête est fautive ?
///
/// <para>Les deux remontaient au même endroit et sortaient avec le même code. ⚠ <b>Ce sont pourtant
/// deux situations sans rapport</b> : l'une se répare dans le code, l'autre se répare en redémarrant
/// un serveur, et l'opérateur à qui l'on répond « Server failure » n'a aucun moyen de savoir
/// laquelle il regarde. Vécu le 13/09/2026 — WSL s'est mis à jour de lui-même, la distribution
/// Docker s'est arrêtée, PostgreSQL avec elle, et <b>chaque écran</b> a répondu 500 avec une trace de
/// pile partant de <c>SyncUserMiddleware</c>. C'est la règle « dire ce que le blanc veut dire »
/// appliquée aux codes d'état, et le pendant du tri des <c>Error.Problem</c> (session 61) pour les
/// exceptions.</para>
/// </summary>
/// <remarks>
/// <para>⚠ <b>La panne doit venir de la couche données, et rien d'autre ne peut la déclarer.</b> Un
/// <c>IOException</c> tout seul n'est pas une panne de base — un export qui n'arrive pas à écrire son
/// fichier en lève un, et le faire passer pour « base injoignable » enverrait chercher la panne là
/// où elle n'est pas. On cherche donc d'abord une <see cref="DbException"/> dans la chaîne, et c'est
/// <i>elle</i> qui est interrogée.</para>
///
/// <para><b>Deux façons pour elle de le dire.</b> <see cref="DbException.IsTransient"/> — que Npgsql
/// renseigne pour un échec de connexion comme pour les états serveur qui se retentent
/// (<c>too_many_connections</c>, <c>cannot_connect_now</c>, sérialisation, verrou mortel) — et, à
/// défaut, la présence d'un échec réseau sous elle. Une <c>PostgresException</c> ordinaire, c'est-à-dire
/// un serveur qui a répondu qu'il refusait la requête, n'est ni l'une ni l'autre : elle reste un
/// <b>défaut</b>, et un défaut doit continuer de se voir comme tel.</para>
/// </remarks>
internal static class DatabaseOutage
{
    /// <summary>Vrai quand l'exception rapporte une base hors d'atteinte plutôt qu'une requête fautive.</summary>
    public static bool IsReported(Exception exception)
    {
        foreach (var link in Chain(exception))
        {
            if (link is DbException database)
                return database.IsTransient || Chain(database).Any(IsUnreachable);
        }

        return false;
    }

    /// <summary>
    /// Ce qu'on trouve sous une connexion qui n'a pas abouti : la prise refusée, le flux coupé, ou
    /// l'attente qui expire.
    /// </summary>
    private static bool IsUnreachable(Exception exception) =>
        exception is SocketException or IOException or TimeoutException;

    /// <summary>
    /// L'exception et tout ce qu'elle enveloppe. ⚠ <c>AggregateException</c> en porte
    /// <i>plusieurs</i> : n'en suivre qu'une est la façon de manquer la seule qui parlait.
    /// </summary>
    private static IEnumerable<Exception> Chain(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                yield return inner;

            yield break;
        }

        if (exception.InnerException is { } single)
        {
            foreach (var inner in Chain(single))
                yield return inner;
        }
    }
}
