using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
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

        // A temporary loan never changes the registration group, so it leaves no GroupTransfer
        // row. Surface it on the student timeline directly (with the destination GROUP + stage)
        // so the outgoing loan and its motif are visible — a definitive move stays an internal,
        // hidden CohortTransfer because the visible GroupTransfer is already written elsewhere.
        if (notification.Type == TransferType.Temporary)
        {
            var from = await GroupAndStageAsync(notification.PreviousCohortId, ct);
            var to   = await GroupAndStageAsync(notification.NewCohortId, ct);

            db.Histories.Add(new History
            {
                Id          = Guid.NewGuid(),
                StudentId   = registration.StudentId,
                HistoryData = HistoryType.GroupTransfer,
                CreatedAt   = DateTime.UtcNow,
                Metadata    = new
                {
                    fromGroup = from.Group,
                    toGroup   = to.Group,
                    stage     = to.Stage ?? from.Stage,
                    reason    = notification.Reason,
                    temporary = true,
                },
            });

            await db.SaveChangesAsync(ct);
            return;
        }

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

    private async Task<(string Group, string? Stage)> GroupAndStageAsync(int cohortId, CancellationToken ct)
    {
        var row = await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => new { Group = c.AcademicGroup.Label, Stage = c.Stage.Name })
            .FirstOrDefaultAsync(ct);

        return (row?.Group ?? $"Cohorte {cohortId}", row?.Stage);
    }
}
