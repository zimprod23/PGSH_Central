using FluentValidation;

namespace PGSH.Application.Extensions;

/// <summary>
/// The page bounds every paginated query validator applies.
/// </summary>
/// <remarks>
/// <para>⚠ <see cref="QueryableExtensions.MaxPageSize"/> is the authority, and it <b>clamps</b>: a
/// page larger than the ceiling is served short, never refused. Three validators spelled their own
/// stricter ceiling (100) with their own message, so a request the pipeline would have served was
/// refused before it ever reached the pipeline.</para>
/// <para>That is not hypothetical. <c>GET /stages?levelId=3&amp;pageSize=200</c> — the CNPN editor's
/// « which stages may this text require » read — 400'd on every open, so the stage list came back
/// empty and the picker rendered as « Tous les stages du niveau sont listés ». Two stages the
/// faculty had just created could not be required by any text, and the refusal read on screen as a
/// disabled control rather than as a rule. One ceiling, stated once.</para>
/// </remarks>
public static class PaginationRules
{
    public static IRuleBuilderOptions<T, int> IsAPageNumber<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(1)
            .WithMessage("Page number must be at least 1.");

    public static IRuleBuilderOptions<T, int> IsAPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, QueryableExtensions.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {QueryableExtensions.MaxPageSize}.");
}
