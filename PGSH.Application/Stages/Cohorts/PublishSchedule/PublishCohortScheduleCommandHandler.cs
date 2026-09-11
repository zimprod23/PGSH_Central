using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishCohortScheduleCommandHandler(
    IApplicationDbContext dbContext,
    SchedulePublisher publisher,
    IAuditTrail auditTrail)
    : ICommandHandler<PublishCohortScheduleCommand>
{
    public async Task<Result> Handle(
        PublishCohortScheduleCommand request, CancellationToken cancellationToken)
    {
        var published = await publisher.PublishCohortAsync(
            request.CohortId, request.AllowOverCapacity, cancellationToken);

        if (published.IsFailure)
            return Result.Failure(published.Error);

        // ⚠ Le publisher a déjà enregistré ses périodes, donc validé l'entrée ouverte par
        // AuditLogPipelineBehavior : le constat déposé ici la remplace, ce qui coûte un DELETE suivi
        // d'un INSERT au lieu d'un seul INSERT. Le registre écrit est le même, et c'est le prix de
        // laisser le publisher maître de son unité de travail — il la partage avec le plan macro,
        // qui n'est pas audité et n'a pas de constat à déposer.
        auditTrail.RecordOutcome(("periodsCreated", published.Value));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
