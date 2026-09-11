using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishStageScheduleCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    SchedulePublisher publisher,
    IAuditTrail auditTrail)
    : ICommandHandler<PublishStageScheduleCommand, PublishResult>
{
    public async Task<Result<PublishResult>> Handle(
        PublishStageScheduleCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PublishResult>(year.Error);

        var published = await publisher.PublishStageAsync(
            request.StageId, year.Value, request.PartitionLabels, request.PeriodNumbers,
            request.AllowOverCapacity, cancellationToken);

        if (published.IsFailure)
            return published;

        var report = published.Value;

        // ⚠ Et le SaveChanges est inconditionnel, là où le publisher n'écrit que s'il a des périodes
        // à poser. « Publier » sur un stage dont tout était déjà publié n'écrivait donc rien du tout
        // au registre — l'acte le plus fréquent d'une campagne, joué une seconde fois, disparaissait.
        // Zéro cohorte publiée n'est pas un non-acte : c'est le constat que tout l'était déjà.
        auditTrail.RecordOutcome(
            ("academicYearId", year.Value),
            ("cohortsPublished", report.PublishedCohorts),
            ("periodsCreated", report.PeriodsCreated),
            ("cohortsSkipped", report.SkippedCohorts),
            ("assignmentsAlreadyServed", report.SkippedAlreadyServed));

        await dbContext.SaveChangesAsync(cancellationToken);

        return report;
    }
}
