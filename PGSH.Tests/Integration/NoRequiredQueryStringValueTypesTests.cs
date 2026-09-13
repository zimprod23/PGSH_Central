using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// No route may require a value type it binds from the query string.
///
/// <para>⚠ <b>This is a hand sweep that earned the right to be a test.</b> A non-nullable value type
/// bound from the query string cannot be omitted: ASP.NET throws <c>BadHttpRequestException</c> inside
/// <c>EndpointMiddleware</c>, <i>before</i> <c>ValidationPipelineBehavior</c>, so the route's own
/// validator is dead code for that request, the caller gets a bare 400 with no <c>detail</c> and no
/// <c>errors[]</c>, and the process pauses for anyone on a debugger.</para>
///
/// <para>⚠ <b>It took the running stack down twice on 13/09/2026</b> — the second time <i>after</i> I
/// had swept for it by hand and declared it closed. The manual sweep scanned one project for the
/// declarations and silently skipped the types declared inside endpoint classes; it also under-counted
/// the parameter types, because an <b>enum</b> is a value type and throws exactly like a
/// <c>DateOnly</c>. Reflection makes neither mistake.</para>
///
/// <para><b>The rule is not « make everything optional ».</b> It is that what a caller can omit must be
/// refused in words, by a validator or by the route, rather than by the model binder. A route-bound
/// parameter (<c>{id:int}</c>) is not affected: it is part of the path, so a request that omits it does
/// not match the route at all.</para>
/// </summary>
public class NoRequiredQueryStringValueTypesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public NoRequiredQueryStringValueTypesTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Routes that already had the defect when this test was written (13/09/2026), left as a shrinking
    /// list rather than fixed in one sweep.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A ratchet, not an amnesty.</b> Each of these needs a <i>named</i> refusal with a sentence —
    /// per-route judgement, not a mechanical edit — and doing twenty of those in one pass is how the
    /// sentences come out generic and useless. What this list buys is that the class cannot <b>grow</b>
    /// while they are worked through: a new route with the defect fails immediately, and removing an
    /// entry here is the definition of done for each. → HANDOFF item 0bs.
    ///
    /// <para>⚠ <b>Nothing may be added to this list.</b> If a change makes it longer, the change is the
    /// bug.</para>
    /// </remarks>
    private static readonly HashSet<string> KnownOffenders =
    [
        "api/groups/all → academicYearId",
        "api/groups/all/students → academicYearId",
        "api/groups/assign-partitions → academicYearId",
        "api/groups/assign-partitions → levelId",
        "api/groups/partitions → academicYearId",
        "api/groups/partitions → levelId",
        "api/hospitals/{hospitalId:int}/stage-coverage → levelId",
        "api/inscription → levelId",
        "api/inscription/preview → levelId",
        "api/inscription/template → levelId",
        "api/levels/{levelId:int}/curriculum/compare → fromCnpnVersionId",
        "api/levels/{levelId:int}/curriculum/compare → toCnpnVersionId",
        "api/registrations/{id:guid} → studentId",
        "api/registrations/{registrationId:guid}/revalidation-context → stageId",
        "api/reinscription/preview → fromAcademicYearId",
        "api/reinscription/preview → toAcademicYearId",
        "api/reinscription/sheet → fromAcademicYearId",
        "api/reinscription/sheet → toAcademicYearId",
        "api/reinscription/sheet/export → fromAcademicYearId",
        "api/reinscription/sheet/export → toAcademicYearId",
        "api/reinscription/sheet/preview → fromAcademicYearId",
        "api/reinscription/sheet/preview → toAcademicYearId",
        "api/stages/{stageId:int}/evaluations/import/template → mode",
        "api/stages/{stageId:int}/evaluations/import/template → scope",
    ];

    [Fact]
    public void No_route_requires_a_value_type_from_the_query_string()
    {
        var offenders = new List<string>();

        foreach (var endpoint in _factory.Services
                     .GetRequiredService<EndpointDataSource>().Endpoints
                     .OfType<RouteEndpoint>())
        {
            var method = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (method is null) continue;

            string route = endpoint.RoutePattern.RawText ?? "";
            var fromRoute = endpoint.RoutePattern.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in method.GetParameters())
            {
                foreach (var member in Bound(parameter))
                {
                    if (fromRoute.Contains(member.Name) || !IsRequiredValueType(member))
                        continue;

                    string offender = $"{route} → {member.Name}";
                    if (!KnownOffenders.Contains(offender))
                        offenders.Add($"{offender} ({member.Type.Name})");
                }
            }
        }

        // Joined into one string so a failure names *every* offender at once. A collection assertion
        // shows the first and hides the rest, which is how a sweep gets declared closed while half of
        // it is still open — the mistake this file exists to stop repeating.
        string.Join(Environment.NewLine, offenders.OrderBy(o => o)).Should().BeEmpty(
            "a required value type bound from the query string throws in routing before any validator "
            + "runs, so the caller gets a bare 400 the client cannot explain. Make it nullable and "
            + "refuse it in words instead.");
    }

    /// <summary>
    /// ⚠ The other half of the ratchet: an entry that has been fixed must be <b>removed</b> from the
    /// list. Without this, the list silently becomes a lie — it would still claim a defect that is no
    /// longer there, and the next reader would trust it.
    /// </summary>
    [Fact]
    public void The_known_offender_list_holds_nothing_that_is_already_fixed()
    {
        var live = new HashSet<string>();

        foreach (var endpoint in _factory.Services
                     .GetRequiredService<EndpointDataSource>().Endpoints
                     .OfType<RouteEndpoint>())
        {
            var method = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (method is null) continue;

            string route = endpoint.RoutePattern.RawText ?? "";
            var fromRoute = endpoint.RoutePattern.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in method.GetParameters())
                foreach (var member in Bound(parameter))
                    if (!fromRoute.Contains(member.Name) && IsRequiredValueType(member))
                        live.Add($"{route} → {member.Name}");
        }

        string.Join(Environment.NewLine, KnownOffenders.Except(live).OrderBy(o => o))
            .Should().BeEmpty("these are fixed — take them out of KnownOffenders");
    }

    /// <summary>
    /// The query-string-bound members of one handler parameter: the parameter itself when it is a bare
    /// value type, or the constructor parameters of an <c>[AsParameters]</c> record.
    /// </summary>
    private static IEnumerable<BoundMember> Bound(ParameterInfo parameter)
    {
        if (parameter.GetCustomAttributes().Any(a => a.GetType().Name == "AsParametersAttribute"))
        {
            var ctor = parameter.ParameterType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            foreach (var p in ctor?.GetParameters() ?? [])
                yield return new BoundMember(parameter.ParameterType.Name, p.Name ?? "", p.ParameterType, p.HasDefaultValue);

            yield break;
        }

        // Services, the body, the cancellation token and IFormFile are not query-string bound.
        if (IsInfrastructure(parameter.ParameterType))
            yield break;

        yield return new BoundMember(
            parameter.Member.DeclaringType?.Name ?? "", parameter.Name ?? "",
            parameter.ParameterType, parameter.HasDefaultValue);
    }

    private static bool IsInfrastructure(Type type) =>
        type == typeof(CancellationToken)
        || typeof(IFormFile).IsAssignableFrom(type)
        || type.Namespace?.StartsWith("MediatR") == true
        || type.Namespace?.StartsWith("Microsoft") == true
        || !type.IsValueType;

    /// <summary>
    /// ⚠ <c>Nullable.GetUnderlyingType</c> is the whole check, and enums must not be excused: an enum
    /// is a value type, so an omitted one throws exactly like a <c>DateOnly</c>. That is the half the
    /// hand sweep missed.
    /// </summary>
    private static bool IsRequiredValueType(BoundMember member) =>
        member.Type.IsValueType
        && Nullable.GetUnderlyingType(member.Type) is null
        && !member.HasDefault;

    private readonly record struct BoundMember(string Declaring, string Name, Type Type, bool HasDefault);
}
