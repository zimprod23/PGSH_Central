using MediatR;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Audit;

namespace PGSH.Application.Behaviors;

public sealed class AuditLogPipelineBehavior<TRequest, TResponse>(
    IApplicationDbContext db,
    IUserContext userContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IAuditableCommand auditable)
        {
            db.AuditLogs.Add(new AuditLog
            {
                PerformedByUserId = TryGetUserId(),
                Action            = auditable.AuditAction,
                EntityType        = auditable.AuditEntityType,
                EntityId          = auditable.AuditEntityId,
                Metadata          = auditable.AuditMetadata,
            });
        }

        return await next();
    }

    private Guid? TryGetUserId()
    {
        try { return userContext.UserId; }
        catch { return null; }
    }
}
