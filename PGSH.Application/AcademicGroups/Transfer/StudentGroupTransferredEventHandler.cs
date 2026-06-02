using MediatR;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;

namespace PGSH.Application.AcademicGroups.Transfer;

internal sealed class StudentGroupTransferredEventHandler(IApplicationDbContext db)
    : INotificationHandler<StudentGroupTransferredDomainEvent>
{
    public async Task Handle(StudentGroupTransferredDomainEvent notification, CancellationToken ct)
    {
        string fromLabel = notification.FromGroupId.HasValue
            ? await db.AcademicGroups
                .AsNoTracking()
                .Where(g => g.Id == notification.FromGroupId.Value)
                .Select(g => g.Label)
                .FirstOrDefaultAsync(ct) ?? $"Groupe {notification.FromGroupId}"
            : "—";

        string toLabel = await db.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == notification.ToGroupId)
            .Select(g => g.Label)
            .FirstOrDefaultAsync(ct) ?? $"Groupe {notification.ToGroupId}";

        db.Histories.Add(new History
        {
            Id          = Guid.NewGuid(),
            StudentId   = notification.StudentId,
            HistoryData = HistoryType.GroupTransfer,
            CreatedAt   = DateTime.UtcNow,
            Metadata    = new
            {
                fromGroup = fromLabel,
                toGroup   = toLabel,
                reason    = notification.Reason,
            },
        });

        await db.SaveChangesAsync(ct);
    }
}
