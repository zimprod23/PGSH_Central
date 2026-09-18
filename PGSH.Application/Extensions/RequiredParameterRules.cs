using FluentValidation;

namespace PGSH.Application.Extensions;

/// <summary>
/// The refusal a parameter gets when a screen can leave it blank.
/// </summary>
/// <remarks>
/// <para>⚠ <b>A non-nullable value type bound from the query string cannot be omitted.</b> ASP.NET
/// raises <c>BadHttpRequestException</c> inside <c>EndpointMiddleware</c>, <i>before</i>
/// <c>ValidationPipelineBehavior</c> runs: the query's own rules are dead code for that request, the
/// caller gets a bare <b>400</b> with no <c>detail</c> and no <c>errors[]</c>, and the client's
/// <c>errorMiddleware</c> shows its generic sentence — the screen reads as broken rather than as
/// « renseignez une promotion ». So the parameter is bound <c>int?</c> / <c>DateOnly?</c> /
/// <c>TEnum?</c> and refused <b>here</b>, in words, with the handler reading it into a non-nullable
/// local.</para>
///
/// <para>⚠ <b>This helper deliberately does not supply the sentence.</b> <see cref="PaginationRules"/>
/// does, because there is genuinely one page ceiling and one thing to say about it. Here there is not:
/// « la promotion est obligatoire » and « précisez l'année de départ » are different facts, and twenty
/// routes fixed with one shared sentence would say no more than the bare 400 they replaced. What is
/// shared is the <i>mechanics</i>, which is the half that has actually gone wrong.</para>
///
/// <para>⚠ <b>Why one predicate rather than <c>NotNull().GreaterThan(0)</c>.</b> <c>.WithMessage</c>
/// attaches to the validator immediately before it, so a chained pair leaves the <i>null</i> case on
/// FluentValidation's default text — « 'Level Id' ne doit pas avoir la valeur null », which names a
/// property and no remedy. That trap has been written by hand twice. A single <c>Must</c> covering
/// both absence and a meaningless value makes it structurally impossible: one predicate, one
/// sentence, nothing to fall back to.</para>
///
/// <para>→ <c>PGSH.Tests/Integration/NoRequiredQueryStringValueTypesTests.cs</c>, HANDOFF item 0bs.</para>
/// </remarks>
public static class RequiredParameterRules
{
    /// <param name="refusal">
    /// What the operator should do, in words. Name the thing that is missing and why the act cannot
    /// proceed without it — never « champ obligatoire », which is what the bare 400 already said.
    /// </param>
    public static IRuleBuilderOptions<T, int?> IsARequiredReference<T>(
        this IRuleBuilder<T, int?> rule, string refusal) =>
        rule.Must(id => id is > 0).WithMessage(refusal);

    public static IRuleBuilderOptions<T, Guid?> IsARequiredReference<T>(
        this IRuleBuilder<T, Guid?> rule, string refusal) =>
        rule.Must(id => id is not null && id != Guid.Empty).WithMessage(refusal);

    public static IRuleBuilderOptions<T, DateOnly?> IsARequiredDate<T>(
        this IRuleBuilder<T, DateOnly?> rule, string refusal) =>
        rule.Must(date => date is not null).WithMessage(refusal);

    /// <summary>
    /// ⚠ An <c>enum</c> is a value type, so an omitted one throws in routing exactly like a
    /// <c>DateOnly</c> — it does <b>not</b> quietly become the zero member. It also has to be a member
    /// that exists: an unparsed value arrives as a number outside the range, and accepted silently it
    /// would pick whichever behaviour the zero member happens to name.
    /// </summary>
    public static IRuleBuilderOptions<T, TEnum?> IsARequiredChoice<T, TEnum>(
        this IRuleBuilder<T, TEnum?> rule, string refusal)
        where TEnum : struct, Enum =>
        rule.Must(value => value is not null && Enum.IsDefined(value.Value)).WithMessage(refusal);
}
