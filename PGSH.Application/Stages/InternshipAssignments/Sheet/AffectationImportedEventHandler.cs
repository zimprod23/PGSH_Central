using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>
/// Puts the declaration in the student's dossier: this rotation was written from a canevas, and this
/// is what it replaced.
/// </summary>
/// <remarks>
/// ⚠ <b><c>dropped</c> is the field that has to be here.</b> After the write there is nothing left to
/// count, and « affectation importée » over an empty stage and over a published rotation are the same
/// sentence about two unrelated events. The audit register carries the promotion-wide total; this
/// carries the one student's, which is the question the dossier is opened to answer.
/// </remarks>
internal sealed class AffectationImportedEventHandler(IApplicationDbContext db)
    : INotificationHandler<AffectationImportedDomainEvent>
{
    public async Task Handle(AffectationImportedDomainEvent notification, CancellationToken ct)
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

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = studentId,
            HistoryData = HistoryType.AffectationImported,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                stage   = stageName ?? $"Stage {notification.StageId}",
                periods = notification.CreatedPeriods,
                dropped = notification.DroppedPeriods,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
