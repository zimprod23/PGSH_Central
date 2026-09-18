using PGSH.SharedKernel;

namespace PGSH.Domain.Users;

public  class User : Entity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Address? Address { get; set; }
    public string? CIN { get; set; }
    public Gender Gender { get; set; }
    public Status Status { get; set; } = new(CivilStatus.Civil, NationalityStatus.Marocaine);
    public DateOnly? DateOfBirth { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? IdentityProviderId { get; private set; }
    public DateTime IdentityLinkedAt { get; private set; }

    /// <summary>
    /// Attaches this record to an identity-provider subject for the first time.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Idempotent for the same subject, and refuses a different one.</b> Re-supplying the
    /// id already held is not a state change and must not be an exception — it is what a retry, a
    /// second tab or an expired sync cache produces. Supplying a <i>different</i> one is a real change
    /// of who this record answers to, and it goes through <see cref="RelinkIdentity"/>, which says so.
    /// </para>
    /// </remarks>
    public void LinkIdentity(string providerId)
    {
        if (IdentityProviderId == providerId)
            return;

        if (IdentityProviderId is not null)
            throw new InvalidOperationException(
                $"User {Id} is already linked to another identity. Re-attaching a record to a new "
                + $"subject is {nameof(RelinkIdentity)}, which records what it replaced.");

        IdentityProviderId = providerId;
        IdentityLinkedAt = DateTime.UtcNow;

        Raise(new UserIdentityLinkedDomainEvent(Id, Email, providerId));
    }

    /// <summary>
    /// Re-attaches this record to a <b>new</b> subject, keeping what it replaced in the event.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> An identity provider can be rebuilt — a realm restored from a
    /// file after its volume was lost, on 17/09/2026 — and it then issues a <i>new</i> subject for the
    /// same person. The database, restored from its own backup, still carries the subject the old
    /// realm issued. `UserContext.SyncAsync` falls back to matching on e-mail precisely for this, and
    /// before this method existed that fallback could not finish: <see cref="LinkIdentity"/> refused,
    /// so **every user who had ever logged in was permanently locked out** by a realm rebuild, with
    /// « User already linked » as the only explanation.</para>
    ///
    /// <para>⚠ <b>It is an event and not a silent assignment.</b> « This account now answers to a
    /// different identity » is a fact about the record, and the previous subject exists nowhere the
    /// second after it is overwritten — the same reason a reversible bulk act records what it
    /// destroyed rather than what it wrote. Without the old value nobody can say whether an account
    /// was re-attached once, by a realm rebuild, or repeatedly.</para>
    ///
    /// <para>⚠ <b>The trust it rests on is the e-mail, and that is the caller's to justify.</b> This
    /// aggregate is told which subject to take; it does not decide that the holder of an address is
    /// the right person. `SyncAsync` may make that call because the realm is the authority on
    /// identity and holds e-mails unique (<c>duplicateEmailsAllowed: false</c>) — a different caller
    /// would have to earn it separately.</para>
    /// </remarks>
    public void RelinkIdentity(string providerId)
    {
        if (IdentityProviderId == providerId)
            return;

        string? previous = IdentityProviderId;

        IdentityProviderId = providerId;
        IdentityLinkedAt = DateTime.UtcNow;

        Raise(new UserIdentityRelinkedDomainEvent(Id, Email, previous, providerId));
    }
}

/// <summary>A record was attached to an identity-provider subject for the first time.</summary>
public sealed record UserIdentityLinkedDomainEvent(
    Guid UserId,
    string Email,
    string ProviderId) : IDomainEvent;

/// <summary>
/// A record was re-attached to a different subject — in practice, an identity provider that was
/// rebuilt. ⚠ Carries <paramref name="PreviousProviderId"/> because it is the half that stops
/// existing the moment the link is overwritten.
/// </summary>
public sealed record UserIdentityRelinkedDomainEvent(
    Guid UserId,
    string Email,
    string? PreviousProviderId,
    string ProviderId) : IDomainEvent;
