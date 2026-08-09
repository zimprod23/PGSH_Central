using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;

namespace PGSH.Application.Students.Registrations;

/// <summary>
/// Writes the year's verdict onto the student's timeline.
/// </summary>
/// <remarks>
/// Deliberately <see cref="HistoryType.StatusChange"/> and not <c>ValidationStage</c> /
/// <c>NonValidation</c>, which <see cref="RegistrationStatusChangedEventHandler"/> uses: those read
/// as a <em>stage</em> being validated, and a year verdict is one level up. Conflating the two is
/// the same mistake the student portal made with Status versus Result.
/// </remarks>
internal sealed class RegistrationYearOutcomeRecordedEventHandler(IApplicationDbContext db)
    : INotificationHandler<RegistrationYearOutcomeRecordedDomainEvent>
{
    public async Task Handle(RegistrationYearOutcomeRecordedDomainEvent notification, CancellationToken ct)
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
                outcome = notification.Outcome.ToString(),
                previousStatus = notification.PreviousStatus.ToString(),
                source = notification.Source.ToString(),
                academicYear = registration.YearLabel,
                level = $"Année {registration.Year} — {registration.AcademicProgram}",
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
