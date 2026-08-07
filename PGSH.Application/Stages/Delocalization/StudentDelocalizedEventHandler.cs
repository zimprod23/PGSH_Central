using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.Delocalization;

internal sealed class StudentDelocalizedEventHandler(IApplicationDbContext db)
    : INotificationHandler<StudentDelocalizedDomainEvent>
{
    public async Task Handle(StudentDelocalizedDomainEvent notification, CancellationToken ct)
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
            HistoryData = HistoryType.Delocalization,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                stage   = stageName  ?? $"Stage {notification.StageId}",
                service = serviceName ?? $"Service {notification.ServiceId}",
                reason  = notification.Reason,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
