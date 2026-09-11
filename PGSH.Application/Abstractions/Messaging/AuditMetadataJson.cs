using System.Text.Json;

namespace PGSH.Application.Abstractions.Messaging;

/// <summary>
/// Builds the JSON object an <see cref="IAuditableCommand"/> puts in
/// <see cref="IAuditableCommand.AuditMetadata"/>.
/// </summary>
/// <remarks>
/// <para>The commands that came first each interpolate their own JSON by hand
/// (<c>$$"""{"levelId":{{LevelId}}}"""</c>). That is fine while every value is an <c>int</c>, and it
/// stops being fine the moment one is <b>free text</b>: a label an admin typed containing a quote or
/// a backslash produces metadata that is not JSON, in the one column whose whole job is to be read
/// back later. <c>CreateGroupCommand</c> carries exactly such a label.</para>
///
/// <para>⚠ <b>The trail is written inside the act's own unit of work</b>
/// (<c>AuditLogPipelineBehavior</c> adds the row and the handler's <c>SaveChanges</c> commits it), so
/// this must never throw: an audit entry that fails to serialise would take the act down with it.
/// Values are therefore limited to what <c>JsonSerializer</c> cannot fail on — numbers, booleans,
/// strings and null.</para>
///
/// <para>Existing hand-written metadata is left alone. This is for new entries and for any old one
/// that turns out to interpolate something a user typed.</para>
/// </remarks>
public static class AuditMetadataJson
{
    /// <summary>
    /// A flat JSON object of the given fields. Returns <c>null</c> when nothing was passed — the
    /// column is nullable and « rien à dire » is better recorded as absent than as <c>{}</c>.
    /// </summary>
    public static string? Of(params (string Key, object? Value)[] fields)
    {
        if (fields.Length == 0)
            return null;

        var payload = new Dictionary<string, object?>(fields.Length);

        foreach (var (key, value) in fields)
            payload[key] = value;

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// <paramref name="json"/> augmenté de <paramref name="fields"/> — ce que la commande a demandé,
    /// plus ce que le handler a constaté. Voir <c>IAuditTrail</c> pour pourquoi les deux moitiés
    /// n'arrivent pas en même temps.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Ne lève pas, pour la raison qui vaut pour <see cref="Of"/></b> : ceci s'exécute dans
    /// l'unité de travail de l'acte, donc une métadonnée illisible ferait tomber l'acte lui-même.
    /// Une valeur déjà présente est écrasée par le constat — c'est le constat qui dit ce qui a eu
    /// lieu, la demande ne disait que ce qui était visé.
    /// </remarks>
    public static string? Merge(string? json, params (string Key, object? Value)[] fields)
    {
        if (fields.Length == 0)
            return json;

        if (string.IsNullOrWhiteSpace(json))
            return Of(fields);

        Dictionary<string, object?>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        // Métadonnée illisible ou qui n'est pas un objet : garder les deux moitiés sous une clé
        // plutôt qu'en perdre une. Le cas n'existe pas aujourd'hui — toutes les métadonnées du dépôt
        // sont des objets plats — et cette branche est ce qui fait que sa découverte ne coûterait ni
        // l'acte, ni son constat.
        payload ??= new Dictionary<string, object?> { ["asked"] = json };

        foreach (var (key, value) in fields)
            payload[key] = value;

        return JsonSerializer.Serialize(payload);
    }
}
