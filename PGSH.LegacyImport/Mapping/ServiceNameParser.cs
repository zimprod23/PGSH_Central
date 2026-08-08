using System.Text.RegularExpressions;
using PGSH.Domain.Hospitals;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// Rebuilds the Hospital → Service hierarchy PGSH requires from the one string the legacy schema has.
/// `SERVICES` is a flat catalogue with no hospital FK and an empty `CHEF_SERV`: the hospital, the
/// service and the responsible professor are all packed into `SERVICE`, e.g.
/// <c>"Hôp.IbnSina: Médecine A - Pr.H.Harmouch"</c>.
///
/// The shapes actually present (all 148 rows were read before writing this):
/// <list type="bullet">
///   <item><c>Hospital: Service - Pr.Name</c> — the common case, with or without the space after ':'</item>
///   <item><c>Hospital: Service Pr.Name</c> — no dash before the professor</item>
///   <item><c>Hôp.Azzamouri Kénitra-Chirurgie</c> — dash instead of colon, no professor</item>
///   <item><c>Santé Publique - Pr.R.Razine</c> — no hospital at all</item>
///   <item><c>Stage délocalisé</c> — neither hospital nor professor</item>
/// </list>
/// </summary>
public static class ServiceNameParser
{
    /// <summary>Where services that name no hospital are parked, so the required FK still resolves.</summary>
    public const string UnknownHospital = "Établissement non précisé";

    public const string DefaultCity = "Rabat";

    // The professor's name, however it is glued on: an optional dash, then a title. Anchored on the
    // title rather than the dash because "Réa-Obs" and "Chirurgie C- Pr.X" both contain dashes that
    // are not separators. First match wins — "Pr.Y.Tadlaoui- Pr.A.Elouartiti" is one two-chef string.
    private static readonly Regex ChefPattern = new(
        @"\s*[-–]?\s*((?:Pr|Dr)\b\.?\s*.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Spelling variants that denote one hospital. Without this, <c>Hôp.Mat.Souissi</c> and
    /// <c>H.Mat.Souissi</c> become two hospitals and the same ward appears twice in the tree.
    /// Cities come from the name where it states one; the rest default to the faculty's own city
    /// (FMPR is in Rabat) — an assumption to correct once, not a fact from the data.
    /// </summary>
    private static readonly Dictionary<string, HospitalIdentity> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hôp.IbnSina"]           = new("Hôpital Ibn Sina", "Rabat", HospitalType.CHU),
        ["HMIMV"]                 = new("Hôpital Militaire Mohammed V", "Rabat", HospitalType.Autre),
        ["Hôp.Enfants"]           = new("Hôpital d'Enfants", "Rabat", HospitalType.CHU),
        ["Hôp.Mly Youssef"]       = new("Hôpital Moulay Youssef", "Rabat", HospitalType.CHU),
        ["Hôp.Spécialités"]       = new("Hôpital des Spécialités", "Rabat", HospitalType.Spetialité),
        ["INO"]                   = new("Institut National d'Oncologie", "Rabat", HospitalType.Spetialité),
        ["Hôp.Mat.Souissi"]       = new("Maternité Souissi", "Rabat", HospitalType.CHU),
        ["H.Mat.Souissi"]         = new("Maternité Souissi", "Rabat", HospitalType.CHU),
        ["Hôp.Lalla Aicha"]       = new("Hôpital Lalla Aicha", "Rabat", HospitalType.CHU),
        ["Hôp.Ar-razi"]           = new("Hôpital Ar-Razi", "Salé", HospitalType.Spetialité),
        ["Hôp Mly.Abdellah Salé"] = new("Hôpital Moulay Abdellah", "Salé", HospitalType.Autre, CityIsStated: true),
        ["Hôp.AlAyachi"]          = new("Hôpital El Ayachi", "Salé", HospitalType.CHU),
        ["Hôp.Orangers"]          = new("Hôpital des Orangers", "Rabat", HospitalType.CHU),
        ["CCTDentaires"]          = new("Centre de Consultation et de Traitement Dentaires", "Rabat", HospitalType.Autre),
        ["CMP.Témara"]            = new("Centre Médico-Psychologique de Témara", "Témara", HospitalType.Autre, CityIsStated: true),
        ["Hôp.Azzamouri Kénitra"] = new("Hôpital Azzamouri", "Kénitra", HospitalType.Autre, CityIsStated: true),
    };

    public static ParsedService Parse(string raw)
    {
        string text = Collapse(raw);

        var (hospitalToken, remainder) = SplitHospital(text);

        string? chef = null;
        var match = ChefPattern.Match(remainder);
        if (match.Success && match.Index > 0)
        {
            chef = Collapse(match.Groups[1].Value);
            remainder = remainder[..match.Index];
        }

        string serviceName = Collapse(remainder).Trim(' ', '-', '–', ':');
        if (serviceName.Length == 0) serviceName = Collapse(text);

        var identity = hospitalToken is null
            ? new HospitalIdentity(UnknownHospital, DefaultCity, HospitalType.None)
            : Resolve(hospitalToken);

        return new ParsedService(identity, serviceName, chef, InferType(serviceName));
    }

    // ':' is the intended separator. A handful of rows use '-' instead, which is only safe to treat
    // as one when the left side actually looks like a hospital — otherwise "Réa-Obs" would be split.
    private static (string? Hospital, string Remainder) SplitHospital(string text)
    {
        int colon = text.IndexOf(':');
        if (colon > 0)
            return (text[..colon].Trim(), text[(colon + 1)..]);

        int dash = text.IndexOf('-');
        if (dash > 0)
        {
            string candidate = text[..dash].Trim();
            if (Known.ContainsKey(candidate) || candidate.StartsWith("Hôp", StringComparison.OrdinalIgnoreCase))
                return (candidate, text[(dash + 1)..]);
        }

        return (null, text);
    }

    private static HospitalIdentity Resolve(string token)
    {
        if (Known.TryGetValue(token, out var known))
            return known;

        // An unlisted prefix is still a hospital — keep its own name rather than lumping it in with
        // the services that name none, and read a city out of it when it states one.
        string? stated = Known.Values
            .Select(h => h.City)
            .Distinct()
            .FirstOrDefault(c => token.Contains(c, StringComparison.OrdinalIgnoreCase));

        return new HospitalIdentity(token, stated ?? DefaultCity, HospitalType.Autre, stated is not null);
    }

    // Matched on accent-stripped text with word boundaries, NOT bare substrings. "Neurologie" contains
    // "urologie", so a naive Contains put the neurology wards in surgery; "Chirurgicale" does not
    // contain "chirurgie", so they were missed the other way. \b is what makes both come out right.
    private static readonly Regex SurgicalPattern = new(
        @"chirurg|\bchir\b|traumato|maxillo|thoracique|vasculaire|\burologie|gyneco|\bobst?\b|ophtalmo|\borl\b",
        RegexOptions.Compiled);

    // Deliberately narrow: the lab-and-dispensary services. "Hemato-clinique" is clinical haematology
    // — a ward that treats patients — so it is Medical, which is why no "hemato" stem appears here.
    private static readonly Regex LaboratoryPattern = new(
        @"pharmacie|transfusion|laboratoire|biologie|banque du sang",
        RegexOptions.Compiled);

    /// <summary>
    /// The legacy catalogue has no service type. This is a starting classification for the hospital
    /// tree, not clinical truth — check it with <c>--review</c> before importing.
    /// </summary>
    private static ServiceType InferType(string serviceName)
    {
        string s = StripAccents(serviceName.ToLowerInvariant());

        if (LaboratoryPattern.IsMatch(s)) return ServiceType.Biologie;
        if (SurgicalPattern.IsMatch(s)) return ServiceType.Chirurgie;
        return ServiceType.Medical;
    }

    private static string StripAccents(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString();
    }

    private static string Collapse(string value) =>
        Regex.Replace(value ?? "", @"\s+", " ").Trim();
}

/// <param name="CityIsStated">
/// True when the legacy string itself named the city (Kénitra, Salé, Témara). False means the city
/// comes from the table above — a reasonable attribution, but not something the data says. Those are
/// the rows worth checking with <c>--review</c>.
/// </param>
public sealed record HospitalIdentity(
    string Name,
    string City,
    HospitalType Type,
    bool CityIsStated = false);

public sealed record ParsedService(
    HospitalIdentity Hospital,
    string Name,
    string? ChefName,
    ServiceType Type);
