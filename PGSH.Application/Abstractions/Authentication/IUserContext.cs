namespace PGSH.Application.Abstractions.Authentication;

public interface IUserContext
{
    Guid UserId { get; }
    Task SyncAsync(CancellationToken cancellationToken = default);
}
