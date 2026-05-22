using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;

namespace PGSH.Application.Students.Registrations;

internal sealed class RegistrationStatusChangedEventHandler(IApplicationDbContext db)
    : INotificationHandler<RegistrationUpdatedDomainEvent>
{
    public async Task Handle(RegistrationUpdatedDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { r.StudentId, r.AcademicYear.Label, r.Level.Year, r.Level.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        var historyType = notification.NewStatus switch
        {
            RegistrationStatus.Active    => HistoryType.Inscription,
            RegistrationStatus.Validated => HistoryType.ValidationStage,
            RegistrationStatus.Failed    => HistoryType.NonValidation,
            _                            => (HistoryType?)null,
        };

        if (historyType is null) return;

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = registration.StudentId,
            HistoryData = historyType.Value,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                status = notification.NewStatus.ToString(),
                academicYear = registration.Label,
                level = $"Année {registration.Year} — {registration.AcademicProgram}",
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
