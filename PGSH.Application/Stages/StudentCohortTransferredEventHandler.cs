using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages;

internal sealed class StudentCohortTransferredEventHandler(IApplicationDbContext db)
    : INotificationHandler<StudentCohortTransferredDomainEvent>
{
    public async Task Handle(StudentCohortTransferredDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { r.StudentId })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        var fromCohort = await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == notification.PreviousCohortId)
            .Select(c => new { c.Label })
            .FirstOrDefaultAsync(ct);

        var toCohort = await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == notification.NewCohortId)
            .Select(c => new { c.Label })
            .FirstOrDefaultAsync(ct);

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = registration.StudentId,
            HistoryData = HistoryType.CohortTransfer,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                from    = fromCohort?.Label ?? $"Cohorte {notification.PreviousCohortId}",
                to      = toCohort?.Label   ?? $"Cohorte {notification.NewCohortId}",
                reason  = notification.Reason,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
