using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Users;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// What happens to a signed-in caller whose local record still carries the subject of an identity
/// provider that no longer exists.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This is not a hypothetical; it is what a restore leaves behind.</b> On 17/09/2026 a
/// Docker volume was lost. The database came back from its own backup carrying the subjects the
/// destroyed Keycloak realm had issued, and the rebuilt realm issued new ones for the same people.
/// <c>UserContext.SyncAsync</c>'s e-mail fallback exists precisely to reunite the two, and it could
/// not: <c>LinkIdentity</c> threw « User already linked » on the stale value, so **every user who had
/// ever logged in was locked out** — after a successful sign-in, with no remedy named anywhere.</para>
///
/// <para>⚠ <b>Only the real pipeline can see this.</b> <c>SyncUserMiddleware</c> runs between
/// authentication and the endpoint; a handler test never reaches it, and the domain tests
/// (<c>UserIdentityLinkTests</c>) prove the rule without proving that this middleware applies it.
/// This is the half that is not the handler.</para>
/// </remarks>
public class RebuiltIdentityProviderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory = factory;

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The subject the destroyed realm had issued, still sitting in the restored row.</summary>
    private static readonly string StaleSubject = Guid.NewGuid().ToString();

    /// <summary>
    /// ⚠ <b>The case that locked everybody out.</b> The caller authenticates with a new subject; the
    /// row carries the old one and the same e-mail. The request must succeed, and the row must end up
    /// pointing at the new subject — otherwise a restored database and a rebuilt realm can never be
    /// reunited without hand-editing the table.
    /// </summary>
    [Fact]
    public async Task A_caller_whose_record_holds_a_subject_from_a_destroyed_realm_can_still_sign_in()
    {
        var newSubject = Guid.NewGuid();

        await _factory.SeedAsync(db =>
        {
            var restored = new User
            {
                Id = Guid.NewGuid(),
                // TestAuthHandler emits this address for that header value — the fallback's only hook.
                Email = $"{newSubject}@integration.test",
                FirstName = "Ahmed",
                LastName = "El Fassi",
            };

            restored.LinkIdentity(StaleSubject);
            db.Users.Add(restored);
        });

        var response = await _factory
            .CreateApiClient(newSubject, Roles.Scolarite)
            .GetAsync("/api/backups/safe-point");

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
            "« User already linked » is what a realm rebuild used to produce on every request");

        response.IsSuccessStatusCode.Should().BeTrue(
            "the e-mail fallback exists to reunite a restored database with a rebuilt realm");

        string? linked = await _factory.QueryAsync(db => db.Users
            .Where(u => u.Email == $"{newSubject}@integration.test")
            .Select(u => u.IdentityProviderId)
            .SingleAsync());

        linked.Should().Be(newSubject.ToString(),
            "the record now answers to the subject the rebuilt realm issues");
    }

    /// <summary>
    /// The control. ⚠ Without it the test above would pass just as well on a pipeline that had stopped
    /// checking anything at all: an e-mail that matches nobody must still be refused, and refused as
    /// <b>403 « Profile Not Found »</b> — the state whose remedy is to create the row, not to re-link.
    /// </summary>
    [Fact]
    public async Task A_caller_no_record_matches_is_still_refused()
    {
        await _factory.SeedAsync(_ => { });

        var response = await _factory
            .CreateApiClient(Guid.NewGuid(), Roles.Scolarite)
            .GetAsync("/api/backups/safe-point");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "no row carries that address, so there is nothing to link and nothing to re-link");
    }
}
