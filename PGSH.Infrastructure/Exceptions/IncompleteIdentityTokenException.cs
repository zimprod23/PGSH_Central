using Microsoft.AspNetCore.Http;

namespace PGSH.Infrastructure.Exceptions;

/// <summary>
/// The caller is authenticated, and the token carries no usable subject — so there is nobody to be.
/// </summary>
/// <remarks>
/// <para><b>What produces it.</b> Not a malformed request and not a missing permission: a token the
/// identity provider issued <i>incompletely</i>. Measured on 17/09/2026, on a realm rebuilt after a
/// lost Docker volume: the client's <c>defaultClientScopes</c> had been written out by hand and
/// omitted <c>basic</c>, which is the scope carrying <c>sub</c> in Keycloak 25. Every token was
/// perfectly valid, perfectly signed, and named nobody.</para>
///
/// <para>⚠ <b>Why 503 and not 401.</b> A 401 is the honest status for « this token will not do », and
/// it is the wrong one here: the client turns a 401 into <c>keycloak.logout()</c>, so a realm issuing
/// subject-less tokens would put a user in a **login loop** — sign in, bounce out, sign in — with
/// nothing anywhere naming the cause. A 500 is worse still: the client discards <c>detail</c> above
/// 500 and shows its fixed « une erreur serveur est survenue ».</para>
///
/// <para>⚠ <b>It is the second thing allowed to claim 503, and the bar is stated rather than
/// stretched:</b> the request wrote nothing, the fault is in a <i>dependency</i> rather than in the
/// request, and the remedy is an operator action on that dependency. <c>DatabaseOutage</c> is the
/// first. An identity provider issuing unusable tokens is that same shape — the application is
/// serving, and one thing it depends on is not.</para>
///
/// <para>⚠ <b>The detail names claim <i>types</i>, never values.</b> What is diagnostic is which
/// claims arrived; what is in them is the user's, and a token's contents in a problem document is a
/// token's contents in a screenshot.</para>
/// </remarks>
public sealed class IncompleteIdentityTokenException(IReadOnlyCollection<string> claimTypesPresent)
    : DomainException(BuildMessage(claimTypesPresent))
{
    public IReadOnlyCollection<string> ClaimTypesPresent { get; } = claimTypesPresent;

    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;

    public override string Title => "Identity Token Incomplete";

    public override string Detail => Message;

    private static string BuildMessage(IReadOnlyCollection<string> claimTypesPresent) =>
        "Le jeton d'identité ne porte aucun identifiant d'utilisateur : ni « sub », ni "
        + "« nameidentifier ». La connexion a réussi et la requête n'a rien écrit ; ce qu'il faut "
        + "corriger est le fournisseur d'identité, pas la demande. Cause la plus fréquente : le client "
        + "Keycloak ne porte pas le scope « basic », qui est celui qui émet « sub ». "
        + (claimTypesPresent.Count == 0
            ? "Aucune revendication n'est arrivée."
            : $"Revendications reçues : {string.Join(", ", claimTypesPresent.Order())}.");
}
