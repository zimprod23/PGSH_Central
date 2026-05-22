using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.InternshipAssignments;

internal sealed class AssignmentValidatedEventHandler(IApplicationDbContext db)
    : INotificationHandler<AssignmentValidatedDomainEvent>
{
    public async Task Handle(AssignmentValidatedDomainEvent notification, CancellationToken ct)
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
            HistoryData = HistoryType.ValidationStage,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                academicYear = registration.Label,
                level        = $"Année {registration.Year} — {registration.AcademicProgram}",
                finalScore   = notification.FinalScore,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
