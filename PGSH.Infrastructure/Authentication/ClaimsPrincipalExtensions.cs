using System.Security.Claims;
using PGSH.Infrastructure.Exceptions;

namespace PGSH.Infrastructure.Authentication;

/// <summary>
/// Reading the caller's identity out of a <see cref="ClaimsPrincipal"/>, without depending on how the
/// pipeline happened to be configured that day.
/// </summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The two spellings the subject can arrive under, in the order they are tried.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Which one it is is a configuration detail, and it must not decide whether anyone can log
    /// in.</b> <c>JwtBearerOptions.MapInboundClaims</c> governs whether <c>sub</c> is rewritten to
    /// <see cref="ClaimTypes.NameIdentifier"/>; the default has moved between handler generations, an
    /// Aspire integration may set it, and a future upgrade may change it again. Reading both is one
    /// line and removes the whole class of failure — reading one was a bet on a default.
    /// </remarks>
    private static readonly string[] SubjectClaimTypes =
    [
        "sub",
        ClaimTypes.NameIdentifier,
    ];

    /// <summary>
    /// The identity provider's subject for this caller.
    /// </summary>
    /// <exception cref="IncompleteIdentityTokenException">
    /// The principal carries no parseable subject under either spelling. ⚠ Deliberately not a bare
    /// <c>ApplicationException</c>: that produced a 500 whose message the client discards, so a realm
    /// issuing subject-less tokens read on screen as « une erreur serveur est survenue » and in the
    /// log as an unhandled exception — a configuration fault wearing the costume of a defect.
    /// </exception>
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        if (TryGetUserId(principal, out Guid userId))
            return userId;

        var present = principal?.Claims.Select(c => c.Type).Distinct().ToList() ?? [];
        throw new IncompleteIdentityTokenException(present);
    }

    /// <summary>
    /// The subject, when there is one — for callers that have something to do about its absence.
    /// </summary>
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;

        if (principal is null)
            return false;

        foreach (string claimType in SubjectClaimTypes)
        {
            if (Guid.TryParse(principal.FindFirstValue(claimType), out userId))
                return true;
        }

        userId = Guid.Empty;
        return false;
    }
}
