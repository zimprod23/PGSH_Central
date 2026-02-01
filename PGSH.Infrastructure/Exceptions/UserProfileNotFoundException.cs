using Microsoft.AspNetCore.Http;

namespace PGSH.Infrastructure.Exceptions;

public sealed class UserProfileNotFoundException : DomainException
{
    public Guid IdentityProviderId { get; }
    public string? Email { get; }

    public UserProfileNotFoundException(Guid id, string? email)
        : base($"Domain profile not found for user with email {email} and provided id {id}")
    {
        IdentityProviderId = id;
        Email = email;
    }

    public override int StatusCode => StatusCodes.Status403Forbidden;
    public override string Title => "Profile Not Found";
}