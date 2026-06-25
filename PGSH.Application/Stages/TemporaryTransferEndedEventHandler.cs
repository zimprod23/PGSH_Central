using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages;

internal sealed class TemporaryTransferEndedEventHandler(IApplicationDbContext db)
    : INotificationHandler<TemporaryTransferEndedDomainEvent>
{
    public async Task Handle(TemporaryTransferEndedDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { r.StudentId })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        var fromGroup = await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == notification.TransferCohortId)
            .Select(c => c.AcademicGroup.Label)
            .FirstOrDefaultAsync(ct);

        var toGroup = await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == notification.OriginalCohortId)
            .Select(c => c.AcademicGroup.Label)
            .FirstOrDefaultAsync(ct);

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = registration.StudentId,
            HistoryData = HistoryType.GroupTransfer,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                fromGroup = fromGroup ?? $"Cohorte {notification.TransferCohortId}",
                toGroup   = toGroup   ?? $"Cohorte {notification.OriginalCohortId}",
                reason    = notification.Reason,
                temporaryReturn = true,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
