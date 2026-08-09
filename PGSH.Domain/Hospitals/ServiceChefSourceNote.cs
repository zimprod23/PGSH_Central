namespace PGSH.Domain.Hospitals;

/// <summary>
/// The legacy note naming a service's chef, kept in <see cref="Service.Description"/> as
/// <c>"Responsable (source) : Pr.A.Settaf"</c>.
///
/// The Access base named the professor as free text and nothing else — no email, no PPR — so the
/// import could not create an <c>Employee</c> for them without inventing an identity, and left
/// <see cref="Service.ServiceChefId"/> null. Measured 2026-08-09: <b>140 of 148 services carry this
/// note and none has a configured chef</b>, so a répartition that reads only the structured field
/// prints no name at all on 95% of its rows.
///
/// ⚠ It is a <b>fallback, not a record</b>. A configured chef comes with a dated tenure
/// (<c>ServiceChefAssignment</c>), so a répartition reprinted years later still names whoever led
/// the service when it was published. This note is undated: it says who the legacy base last
/// recorded, and reprinting cannot make it as-of anything. Callers surface the difference rather
/// than passing it off as the same fact — hence <c>ChefIsFromSourceNote</c> on the response.
///
/// Writing it is the importer's job (<c>LegacyImportPlanner</c>); this type owns the format so the
/// two ends cannot drift.
/// </summary>
public static class ServiceChefSourceNote
{
    public const string Prefix = "Responsable (source) : ";

    public static string Format(string chefName) => $"{Prefix}{chefName}";

    /// <summary>
    /// The chef named by <paramref name="description"/>, or null when it carries no such note.
    /// Several names separated as the source wrote them (<c>"Pr.Y.Tadlaoui- Pr.A.Elouartiti"</c>) are
    /// returned whole: a service genuinely led by two people should print both, and re-splitting on
    /// a hyphen would break the compound surnames that also occur.
    /// </summary>
    public static string? Read(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var text = description.AsSpan().Trim();
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var name = text[Prefix.Length..].Trim();
        return name.IsEmpty ? null : name.ToString();
    }
}
