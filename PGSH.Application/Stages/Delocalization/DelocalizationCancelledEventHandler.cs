using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.Delocalization;

internal sealed class DelocalizationCancelledEventHandler(IApplicationDbContext db)
    : INotificationHandler<DelocalizationCancelledDomainEvent>
{
    public async Task Handle(DelocalizationCancelledDomainEvent notification, CancellationToken ct)
    {
        var studentId = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => r.StudentId)
            .FirstOrDefaultAsync(ct);

        if (studentId == Guid.Empty) return;

        var stageName = await db.Stages
            .AsNoTracking()
            .Where(s => s.Id == notification.StageId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);

        var serviceName = await db.Services
            .AsNoTracking()
            .Where(s => s.Id == notification.ServiceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = studentId,
            HistoryData = HistoryType.DelocalizationCancelled,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                stage   = stageName   ?? $"Stage {notification.StageId}",
                service = serviceName ?? $"Service {notification.ServiceId}",
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
