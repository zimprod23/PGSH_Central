using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.Revalidation;

internal sealed class StageRevalidationOpenedEventHandler(IApplicationDbContext db)
    : INotificationHandler<StageRevalidationOpenedDomainEvent>
{
    public async Task Handle(StageRevalidationOpenedDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { r.StudentId, r.AcademicYear.Label, r.Level.Year, r.Level.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        var previousYear = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.PreviousRegistrationId)
            .Select(r => r.AcademicYear.Label)
            .FirstOrDefaultAsync(ct);

        var stageName = await db.Stages
            .AsNoTracking()
            .Where(s => s.Id == notification.StageId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = registration.StudentId,
            HistoryData = HistoryType.Revalidation,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                academicYear = registration.Label,
                level        = $"Année {registration.Year} — {registration.AcademicProgram}",
                stageId      = notification.StageId,
                stage        = stageName,
                failedInYear = previousYear,
                reason       = notification.Reason,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
