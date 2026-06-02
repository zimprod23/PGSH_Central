using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.InternshipAssignments;

internal sealed class AssignmentRejectedEventHandler(IApplicationDbContext db)
    : INotificationHandler<AssignmentRejectedDomainEvent>
{
    public async Task Handle(AssignmentRejectedDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { r.StudentId, r.AcademicYear.Label, r.Level.Year, r.Level.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = registration.StudentId,
            HistoryData = HistoryType.NonValidation,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                academicYear = registration.Label,
                level        = $"Année {registration.Year} — {registration.AcademicProgram}",
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
