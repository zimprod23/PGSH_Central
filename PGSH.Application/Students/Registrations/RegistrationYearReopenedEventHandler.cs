using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;

namespace PGSH.Application.Students.Registrations;

/// <summary>
/// Writes the withdrawal of a year's verdict onto the student's timeline.
/// </summary>
/// <remarks>
/// <see cref="HistoryType.StatusChange"/> like the verdict itself — a reopening is a status change, not
/// a stage event — and the metadata carries <c>reopened</c> so the two are never read as one. A
/// timeline that showed only the new verdict, with no trace of the one taken back, is how a student's
/// file quietly stops matching the PV it was built from.
/// </remarks>
internal sealed class RegistrationYearReopenedEventHandler(IApplicationDbContext db)
    : INotificationHandler<RegistrationYearReopenedDomainEvent>
{
    public async Task Handle(RegistrationYearReopenedDomainEvent notification, CancellationToken ct)
    {
        var registration = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Id == notification.RegistrationId)
            .Select(r => new { YearLabel = r.AcademicYear.Label, r.Level.Year, r.Level.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (registration is null) return;

        db.Histories.Add(new History
        {
            Id = Guid.NewGuid(),
            StudentId = notification.StudentId,
            HistoryData = HistoryType.StatusChange,
            CreatedAt = DateTime.UtcNow,
            Metadata = new
            {
                reopened = true,
                withdrawnOutcome = notification.WithdrawnOutcome.ToString(),
                reason = notification.Reason,
                academicYear = registration.YearLabel,
                level = $"Année {registration.Year} — {registration.AcademicProgram}",
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
