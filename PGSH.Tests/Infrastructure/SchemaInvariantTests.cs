using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Registrations;
using Xunit;

namespace PGSH.Tests.Infrastructure;

/// <summary>
/// Invariants the <b>schema</b> is supposed to enforce, asserted against the EF model.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Why this file exists, and what it does and does not prove.</b> Some rules are stated
/// twice on purpose: once in the aggregate, which is where the sentence lives, and once as a unique
/// index, which is what survives a caller getting the aggregate's preconditions wrong. The second
/// statement is invisible to the rest of this suite — <c>UseInMemoryDatabase</c> ignores unique
/// indexes entirely — so nothing here executes SQL. It reads the <b>model</b> built for the Npgsql
/// provider and asserts that the index is declared, which is the half that can be checked without a
/// database.</para>
///
/// <para>It proves the index is <em>configured</em>. It does not prove PostgreSQL enforces it; that
/// still needs Testcontainers. What it does catch is the index being dropped, renamed, or having its
/// filter or column list quietly changed — which is how a guard of this kind is usually lost.</para>
/// </remarks>
public class SchemaInvariantTests
{
    /// <summary>
    /// One standing signalement per (inscription, motif), any number of released ones behind it.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The incident this comes from.</b> <c>Registration.PlaceOnHold</c> is idempotent per
    /// reason by reading <c>Registration.Holds</c> — and an un-Included collection is
    /// indistinguishable from an empty one. The réinscription roll's closing-year query did not
    /// Include it, so the second upload raised a <em>second</em> absentee flag on all 1 267 of them.
    /// Measured on the live base 2026-09-02.</para>
    ///
    /// <para>The in-memory suite could not see it: that provider fixes navigations up from the change
    /// tracker, so the idempotency test passed the whole time. The index is what turns the next such
    /// omission into a constraint violation instead of silent duplication — the same bargain
    /// <c>IX_CnpnLevelEffectivity_Version_Level</c> strikes.</para>
    /// </remarks>
    [Fact]
    public void A_registration_carries_at_most_one_standing_hold_per_reason()
    {
        using var db = TestHarness.NewNpgsqlContext();

        var index = db.Model
            .FindEntityType(typeof(RegistrationHold))!
            .GetIndexes()
            .SingleOrDefault(i =>
                i.GetDatabaseName() == "IX_RegistrationHold_Registration_Reason_Active");

        index.Should().NotBeNull(
            "the aggregate's idempotency check reads a collection that a caller can forget to Include");

        index!.IsUnique.Should().BeTrue("otherwise it documents the rule without enforcing it");

        index.Properties.Select(p => p.Name)
            .Should().Equal([nameof(RegistrationHold.RegistrationId), nameof(RegistrationHold.Reason)],
                "per reason, not per registration — two different reasons legitimately coexist");

        index.GetFilter().Should().Contain("ReleasedOn",
            "released rows are history and any number of them may stand behind a live flag");
    }
}
