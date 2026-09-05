namespace PGSH.Application.Hospitals.Chefs;

/// <summary>
/// The resolved answer to « qui dirige ce service ? », sent rather than re-derived.
///
/// <para>⚠ <b>This exists because the screen and the documents disagreed, and it cost a real « d'où
/// sort ce nom ? » on 2026-09-03.</b> The service fiche ranked the sources itself — the sitting FK
/// (null on all 148 services), then the note, with the open tenure filed under « Historique » —
/// while <see cref="ServiceChefDirectory"/> ranked them the other way. One rule, two sides of a
/// network boundary, nothing able to catch them drifting: the same class as
/// <c>ServicePeriodResponse.State</c>, and the same fix.</para>
///
/// <para><b>It lives here, beside the directory, and not in one query's folder</b> — the fiche, the
/// services list and the student portal all print it, and a response type owned by whichever screen
/// happened to need it first is how the second screen ends up with its own copy. Same rule as
/// <c>UserResponse</c> and <c>LevelResponse</c>.</para>
/// </summary>
/// <param name="Name">Null when nobody is named at all — « aucun chef désigné ».</param>
/// <param name="FromSourceNote">
/// The name is the <b>undated</b> import note rather than a dated affectation. Never dropped beside
/// the name: printing an undated note as the record is a claim nothing supports.
/// </param>
/// <param name="LinkedChefWithheld">
/// ⚠ A chef <em>is</em> linked in Personnel and is deliberately not the name above — the temporary
/// <see cref="ServiceChefPolicy.InForce"/> = <see cref="ServiceChefSourcePolicy.SourceNoteOnly"/>.
/// Without this a page shows an « en cours » tenure under a headline naming somebody else and
/// explains neither, which is the confusion this whole shape removes. False when nobody is linked:
/// that is a different sentence.
/// </param>
public sealed record ServiceChefAttributionResponse(
    string? Name,
    bool FromSourceNote,
    bool LinkedChefWithheld)
{
    /// <summary>
    /// One service's answer, read off an already-built <paramref name="chefs"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The ceremony is here so no caller invents its own.</b> Resolving the name and asking
    /// whether a linked chef is being withheld are two calls that must be made with the <em>same</em>
    /// as-of date and the same directory; split across screens, one of them eventually forgets the
    /// second — and a page that cannot say « quelqu'un est rattaché, et ce n'est pas ce nom-là » is
    /// exactly the page that produced the original confusion.
    /// <para><paramref name="asOf"/> is the caller's, never assumed: a fiche asks « qui dirige ce
    /// service ? » (today), a document asks « qui le dirigeait quand ceci a été publié ? ».</para>
    /// </remarks>
    public static ServiceChefAttributionResponse From(
        ServiceChefDirectory chefs, int serviceId, DateOnly asOf)
    {
        var attribution = chefs.For(serviceId, asOf);

        return new ServiceChefAttributionResponse(
            attribution.Name,
            attribution.FromSourceNote,
            chefs.HasWithheldLinkedChef(serviceId, asOf));
    }
}
