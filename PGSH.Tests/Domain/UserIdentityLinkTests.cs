using FluentAssertions;
using PGSH.Domain.Users;
using Xunit;

namespace PGSH.Tests.Domain;

/// <summary>
/// Attaching a <see cref="User"/> record to an identity-provider subject, and re-attaching it when
/// the provider is rebuilt.
/// </summary>
/// <remarks>
/// <para>⚠ <b>The case these exist for is not exotic — it is what a restore produces.</b> On
/// 17/09/2026 a Docker volume was lost. The database came back from its own backup still carrying the
/// subjects the destroyed Keycloak realm had issued; the rebuilt realm issued new ones.
/// <c>UserContext.SyncAsync</c> falls back to matching on e-mail precisely for that, and could not
/// finish: <c>LinkIdentity</c> threw « User already linked ». **Every user who had ever logged in was
/// locked out of the application, permanently, by an identity provider being rebuilt** — and the
/// message named no remedy.</para>
///
/// <para>Nothing covered this before, because nothing had ever rebuilt the realm.</para>
/// </remarks>
public class UserIdentityLinkTests
{
    private const string OldRealmSubject = "6f1c5a0e-1111-4aaa-9bbb-000000000001";
    private const string NewRealmSubject = "97634215-7e52-4138-890b-b85a88b6ff4b";

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin.pgsh@um5.ac.ma",
        FirstName = "Ahmed",
        LastName = "El Fassi",
    };

    [Fact]
    public void A_fresh_record_takes_its_first_subject()
    {
        var user = NewUser();

        user.LinkIdentity(NewRealmSubject);

        user.IdentityProviderId.Should().Be(NewRealmSubject);
        user.IdentityLinkedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        user.DomainEvents.Should().ContainSingle(e => e is UserIdentityLinkedDomainEvent);
    }

    /// <summary>
    /// ⚠ Supplying the subject already held is not a state change, so it is not an error either. A
    /// retry, a second tab, or a sync cache that has expired all produce exactly this call, and
    /// throwing on it turns ordinary traffic into 500s.
    /// </summary>
    [Fact]
    public void Linking_the_same_subject_twice_is_a_no_op_rather_than_a_refusal()
    {
        var user = NewUser();
        user.LinkIdentity(NewRealmSubject);
        user.ClearDomainEvents();

        var act = () => user.LinkIdentity(NewRealmSubject);

        act.Should().NotThrow();
        user.IdentityProviderId.Should().Be(NewRealmSubject);
        user.DomainEvents.Should().BeEmpty("nothing changed, so nothing happened");
    }

    /// <summary>
    /// ⚠ The refusal that stays: silently re-pointing a record at a different identity is exactly what
    /// must not happen by accident. What changed is that there is now a named act for doing it on
    /// purpose, and the message says so instead of « User already linked ».
    /// </summary>
    [Fact]
    public void Linking_a_different_subject_is_refused_and_names_the_act_that_is_allowed()
    {
        var user = NewUser();
        user.LinkIdentity(OldRealmSubject);

        var act = () => user.LinkIdentity(NewRealmSubject);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(User.RelinkIdentity)}*",
                "a refusal that names no remedy is how this locked everybody out");

        user.IdentityProviderId.Should().Be(OldRealmSubject, "the refusal wrote nothing");
    }

    /// <summary>
    /// ⚠ <b>The whole point.</b> A rebuilt realm issues a new subject for the same person, and the
    /// record must follow — otherwise a restored database and a restored realm can never be reunited.
    /// </summary>
    [Fact]
    public void A_rebuilt_provider_can_reattach_a_record_that_carries_a_stale_subject()
    {
        var user = NewUser();
        user.LinkIdentity(OldRealmSubject);
        user.ClearDomainEvents();

        user.RelinkIdentity(NewRealmSubject);

        user.IdentityProviderId.Should().Be(NewRealmSubject);
    }

    /// <summary>
    /// ⚠ The previous subject travels in the event, because it exists nowhere the second after the
    /// link is overwritten. Same rule as a reversible bulk act recording what it <i>destroyed</i>
    /// rather than what it wrote: without it, nobody can say whether an account was re-attached once
    /// by a realm rebuild or repeatedly by something else.
    /// </summary>
    [Fact]
    public void Re_linking_carries_the_subject_it_replaced()
    {
        var user = NewUser();
        user.LinkIdentity(OldRealmSubject);
        user.ClearDomainEvents();

        user.RelinkIdentity(NewRealmSubject);

        var raised = user.DomainEvents.OfType<UserIdentityRelinkedDomainEvent>().Should().ContainSingle().Subject;

        raised.PreviousProviderId.Should().Be(OldRealmSubject);
        raised.ProviderId.Should().Be(NewRealmSubject);
        raised.Email.Should().Be(user.Email);
    }

    [Fact]
    public void Re_linking_to_the_subject_already_held_changes_nothing_and_says_nothing()
    {
        var user = NewUser();
        user.LinkIdentity(NewRealmSubject);
        user.ClearDomainEvents();

        user.RelinkIdentity(NewRealmSubject);

        user.DomainEvents.Should().BeEmpty();
    }
}
