using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

internal sealed class CreateStageSlotCommandHandler(
    IApplicationDbContext dbContext,
    SlotOverlapGuard overlapGuard)
    : ICommandHandler<CreateStageSlotCommand, int>
{
    public async Task<Result<int>> Handle(CreateStageSlotCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages.AnyAsync(s => s.Id == request.StageId, cancellationToken);
        if (!stageExists)
            return Result.Failure<int>(StageErrors.NotFound(request.StageId));

        bool yearExists = await dbContext.AcademicYears
            .AnyAsync(y => y.Id == request.AcademicYearId, cancellationToken);
        if (!yearExists)
            return Result.Failure<int>(StageErrors.AcademicYearNotFound(request.AcademicYearId));

        bool duplicate = await dbContext.StageSlots
            .AnyAsync(s => s.StageId == request.StageId
                        && s.AcademicYearId == request.AcademicYearId
                        && s.PeriodNumber == request.PeriodNumber, cancellationToken);
        if (duplicate)
            return Result.Failure<int>(StageErrors.DuplicatePeriodNumber(request.PeriodNumber));

        var overlap = await overlapGuard.EnsureNoOverlapAsync(
            request.StageId, request.AcademicYearId, request.PeriodNumber, request.StartDate, request.EndDate,
            excludedSlotId: null, cancellationToken);
        if (overlap.IsFailure)
            return Result.Failure<int>(overlap.Error);

        var made = StageSlot.For(
            request.StageId, request.AcademicYearId, request.PeriodNumber,
            request.StartDate, request.EndDate, request.Label);

        if (made.IsFailure)
            return Result.Failure<int>(made.Error);

        var slot = made.Value;

        dbContext.StageSlots.Add(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return slot.Id;
    }
}

internal sealed class UpdateStageSlotCommandHandler(
    IApplicationDbContext dbContext,
    SlotOverlapGuard overlapGuard,
    GroupScheduleConflictGuard groupGuard,
    PublishedPeriodShifter shifter,
    IAuditTrail auditTrail)
    : ICommandHandler<UpdateStageSlotCommand, StageSlotMoveResult>
{
    /// <remarks>
    /// ⚠ <b>Atomique, et par la piste plutôt que par le contexte.</b> Le créneau et les périodes
    /// publiées qui en viennent sont deux écritures : s'arrêter entre les deux laisse exactement
    /// l'état que cet acte existe pour supprimer — une colonne à ses nouvelles dates et des périodes
    /// aux anciennes. Et <c>IAuditTrail.RunAtomicallyAsync</c> plutôt que
    /// <c>ExecuteAtomicallyAsync</c>, parce qu'un réessai vide le change tracker et que seule la
    /// piste sait quelle version de son entrée est la bonne.
    /// </remarks>
    public Task<Result<StageSlotMoveResult>> Handle(
        UpdateStageSlotCommand request, CancellationToken cancellationToken) =>
        auditTrail.RunAtomicallyAsync(ct => MoveAsync(request, ct), cancellationToken);

    private async Task<Result<StageSlotMoveResult>> MoveAsync(
        UpdateStageSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await dbContext.StageSlots
            .FirstOrDefaultAsync(s => s.Id == request.SlotId && s.StageId == request.StageId, cancellationToken);

        if (slot is null)
            return Result.Failure<StageSlotMoveResult>(StageErrors.SlotNotFound(request.SlotId));

        // ⚠ **Ce que fait la moitié publiée, décidé avant toute écriture.** Jusqu'au 13/09/2026 cet
        // acte *refusait* une colonne publiée (`Schedule.SlotPublishedCannotMove`), parce que déplacer
        // la colonne sans ses périodes réussit et désynchronise en silence — le créneau prend ses
        // nouvelles dates, les périodes gardent les anciennes, et aucun écran ne dit laquelle est
        // vraie. Le seul remède offert était « dépubliez d'abord », ce qui sur une promotion publiée
        // en entier veut dire détruire l'année pour décaler une semaine. C'est la phase 17.1 : les
        // deux moitiés bougent ensemble ou rien ne bouge.
        var plan = await shifter.PlanAsync(slot.Id, request.StartDate, request.EndDate, cancellationToken);
        if (plan.IsFailure)
            return Result.Failure<StageSlotMoveResult>(plan.Error);

        // ⚠ Le nombre montré, comparé à ce qu'on trouve — jamais une case à cocher. Une période
        // évaluée entre l'aperçu et l'application est précisément le cas qu'un booléen laisse passer.
        if (plan.Value.PeriodsAffected > 0 && request.ConfirmedPeriodCount is null)
            return Result.Failure<StageSlotMoveResult>(
                StageErrors.SlotMoveNotConfirmed(plan.Value.PeriodsAffected));

        if (plan.Value.PeriodsAffected > 0 && request.ConfirmedPeriodCount != plan.Value.PeriodsAffected)
            return Result.Failure<StageSlotMoveResult>(StageErrors.SlotMoveCountMismatch(
                request.ConfirmedPeriodCount!.Value, plan.Value.PeriodsAffected));

        // Moving a period must respect the same rule as creating one — it is the same collision,
        // just reached by editing dates instead of adding a row.
        var overlap = await overlapGuard.EnsureNoOverlapAsync(
            request.StageId, slot.AcademicYearId, slot.PeriodNumber, request.StartDate, request.EndDate,
            excludedSlotId: slot.Id, cancellationToken);
        if (overlap.IsFailure)
            return Result.Failure<StageSlotMoveResult>(overlap.Error);

        // Dragging a period onto another stage's window double-books every group already in it,
        // without any cell being touched. The per-stage overlap check above cannot see that.
        var free = await groupGuard.EnsureSlotCanMoveAsync(
            slot.Id, request.StartDate, request.EndDate, cancellationToken);
        if (free.IsFailure)
            return Result.Failure<StageSlotMoveResult>(free.Error);

        // ⚠ Relevées avant l'écrasement : une fois la ligne écrite, les anciennes dates n'existent
        // plus nulle part, et « d'où ce créneau a-t-il été déplacé » est la question qu'on pose au
        // registre après avoir vu une promotion décalée. Les deux comptes y sont aussi, parce que le
        // code seul ne distingue pas « colonne vide déplacée » de « 7 464 périodes réécrites ».
        auditTrail.RecordOutcome(
            ("academicYearId", slot.AcademicYearId),
            ("periodNumber", slot.PeriodNumber),
            ("fromStartDate", slot.StartDate.ToString("yyyy-MM-dd")),
            ("fromEndDate", slot.EndDate.ToString("yyyy-MM-dd")),
            ("periodsCovered", plan.Value.PeriodsAffected),
            ("periodsShifted", plan.Value.PeriodsWhoseWindowChanges));

        slot.Label     = request.Label;
        slot.StartDate = request.StartDate;
        slot.EndDate   = request.EndDate;

        var shifted = await shifter.ApplyAsync(plan.Value, cancellationToken);
        if (shifted.IsFailure)
            return Result.Failure<StageSlotMoveResult>(shifted.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StageSlotMoveResult(
            plan.Value.PeriodsWhoseWindowChanges, plan.Value.PeriodsAffected);
    }
}

internal sealed class DeleteStageSlotCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<DeleteStageSlotCommand>
{
    public async Task<Result> Handle(DeleteStageSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await dbContext.StageSlots
            .FirstOrDefaultAsync(s => s.Id == request.SlotId, cancellationToken);

        if (slot is null)
            return Result.Failure(StageErrors.SlotNotFound(request.SlotId));

        bool hasPublishedCells = await dbContext.SlotHasPublishedCellAsync(request.SlotId, cancellationToken);

        if (hasPublishedCells)
            return Result.Failure(StageErrors.SlotPublished);

        // Un identifiant de créneau ne survit pas à sa ligne : sans ces champs l'entrée nommerait
        // une colonne que plus rien ne permet de situer.
        auditTrail.RecordOutcome(
            ("stageId", slot.StageId),
            ("academicYearId", slot.AcademicYearId),
            ("periodNumber", slot.PeriodNumber),
            ("startDate", slot.StartDate.ToString("yyyy-MM-dd")),
            ("endDate", slot.EndDate.ToString("yyyy-MM-dd")));

        dbContext.StageSlots.Remove(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetCohortSlotAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    GroupScheduleConflictGuard groupGuard,
    IAuditTrail auditTrail)
    : ICommandHandler<SetCohortSlotAssignmentCommand, int>
{
    public async Task<Result<int>> Handle(SetCohortSlotAssignmentCommand request, CancellationToken cancellationToken)
    {
        bool cohortExists = await dbContext.Cohorts.AnyAsync(c => c.Id == request.CohortId, cancellationToken);
        if (!cohortExists)
            return Result.Failure<int>(StageErrors.CohortNotFound(request.CohortId));

        bool slotExists = await dbContext.StageSlots.AnyAsync(s => s.Id == request.StageSlotId, cancellationToken);
        if (!slotExists)
            return Result.Failure<int>(StageErrors.SlotNotFound(request.StageSlotId));

        // ⚠ Asked for the flag, not merely for existence. 25 of the 27 stages authorise no service at
        // all, so the whitelist below guards nothing on them and a cell could be placed by hand on a
        // hospital the faculty does not run — which would put an invented ceiling into the saturation
        // of the very grid a délocalisation exists to relieve.
        var service = await dbContext.Services
            .AsNoTracking()
            .Where(s => s.Id == request.ServiceId)
            .Select(s => new { s.Id, s.IsExternal })
            .FirstOrDefaultAsync(cancellationToken);

        if (service is null)
            return Result.Failure<int>(Error.NotFound("Services.NotFound", $"Service {request.ServiceId} was not found."));

        if (service.IsExternal)
            return Result.Failure<int>(StageErrors.ExternalServiceNotAllowedInStage);

        // Enforce allowed-services whitelist when configured
        int stageId = await dbContext.StageSlots
            .Where(s => s.Id == request.StageSlotId)
            .Select(s => s.StageId)
            .FirstAsync(cancellationToken);

        bool hasWhitelist = await dbContext.Stages
            .AnyAsync(s => s.Id == stageId && s.AllowedServices.Any(), cancellationToken);

        if (hasWhitelist)
        {
            bool allowed = await dbContext.Stages
                .AnyAsync(s => s.Id == stageId && s.AllowedServices.Any(svc => svc.Id == request.ServiceId), cancellationToken);

            if (!allowed)
                return Result.Failure<int>(StageErrors.ServiceNotAllowed(request.ServiceId, stageId));
        }

        // ⚠ **Cohorte-wide, and deliberately so — this is not `IsCellPublishedAsync`'s job.** It reads
        // like the imprecise version of « est-ce que *cette* cellule est publiée », and it was filed as
        // that; it is not. Publication is **once per cohorte** — `SchedulePublisher.PublishCohortAsync`
        // refuses outright when any assignment already carries a published période — so once a cohorte
        // is published, no later publication will ever read its cells again. Narrowing this to the cell
        // would let an edit *appear* to work and produce nothing, which is the « un seul état pour deux
        // situations » defect this codebase exists to hunt, arrived at from the helpful direction.
        //
        // The refusal names the remedy, and the remedy is real: unpublish, edit, republish.
        bool isPublished = await dbContext.IsCohortSchedulePublishedAsync(request.CohortId, cancellationToken);

        if (isPublished)
            return Result.Failure<int>(StageErrors.ScheduleAlreadyPublished);

        var existing = await dbContext.CohortSlotAssignments
            .FirstOrDefaultAsync(a => a.CohortId == request.CohortId && a.StageSlotId == request.StageSlotId, cancellationToken);

        if (existing is not null)
        {
            // Only the service changes; the group already occupies this period legitimately, so
            // there is nothing new to conflict with.
            //
            // ⚠ And the cell becomes pinned whether or not it already was: overwriting a service the
            // arranger chose IS the human decision, so leaving it Arranged would let the next
            // auto-arrange undo the correction that was just made — the exact defect the marker
            // exists to close, arrived at from the other direction.
            // ⚠ Le service d'avant, et si la cellule était déjà une décision humaine : épingler une
            // cellule vide et écraser le choix de quelqu'un d'autre sont deux actes, et le code seul
            // ne les distingue pas.
            auditTrail.RecordOutcome(
                ("replacedServiceId", existing.ServiceId),
                ("wasPinned", existing.IsPinned));

            existing.ServiceId = request.ServiceId;
            existing.Source    = CellSource.Pinned;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var free = await groupGuard.EnsureGroupIsFreeAsync(
            request.CohortId, request.StageSlotId, cancellationToken);
        if (free.IsFailure)
            return Result.Failure<int>(free.Error);

        var assignment = new CohortSlotAssignment
        {
            CohortId    = request.CohortId,
            StageSlotId = request.StageSlotId,
            ServiceId   = request.ServiceId,
            Source      = CellSource.Pinned,
        };

        auditTrail.RecordOutcome(("replacedServiceId", null), ("wasPinned", false));

        dbContext.CohortSlotAssignments.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return assignment.Id;
    }
}

internal sealed class ClearCohortSlotAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<ClearCohortSlotAssignmentCommand>
{
    public async Task<Result> Handle(ClearCohortSlotAssignmentCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.CohortSlotAssignments
            .FirstOrDefaultAsync(a => a.CohortId == request.CohortId && a.StageSlotId == request.StageSlotId, cancellationToken);

        // ⚠ Vider une cellule déjà vide réussit, et s'enregistre comme tel : le registre a besoin de
        // séparer « personne n'a joué cet acte » de « quelqu'un l'a joué sans effet ».
        if (existing is null)
        {
            auditTrail.RecordOutcome(("cellExisted", false), ("clearedServiceId", null));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        // ⚠ **Asked of the cohorte, not of the cell — corrected 13/09/2026, and the asymmetry was the
        // defect.** This handler asked « est-ce que *cette* cellule est publiée » while its twin
        // `SetCohortSlotAssignment` asks « est-ce que cette *cohorte* est publiée ». On a published
        // cohorte you could therefore **clear an unpublished cell and not put it back**: a hole nobody
        // can fill without unpublishing the whole cohorte, which destroys and rebuilds everything else
        // with it.
        //
        // Resolved towards the stricter of the two because that is the one the publication model makes
        // true: publication is once per cohorte, so a cell of a published cohorte is not editable in
        // either direction — and « destroy what cannot be restored » is exactly what a guard is for.
        //
        // ⚠ The *bulk* `ClearSlotAssignmentsCommandHandler` stays per-cell on purpose: it sweeps a whole
        // column across a promotion, keeps the published cells, and **reports both numbers**. That is a
        // different act, and it says what it kept.
        bool isPublished = await dbContext.IsCohortSchedulePublishedAsync(request.CohortId, cancellationToken);

        if (isPublished)
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        auditTrail.RecordOutcome(
            ("cellExisted", true),
            ("clearedServiceId", existing.ServiceId),
            ("wasPinned", existing.IsPinned));

        dbContext.CohortSlotAssignments.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ClearSlotAssignmentsCommandHandler(
    IApplicationDbContext dbContext,
    IAuditTrail auditTrail)
    : ICommandHandler<ClearSlotAssignmentsCommand, ClearSlotResult>
{
    public async Task<Result<ClearSlotResult>> Handle(ClearSlotAssignmentsCommand request, CancellationToken cancellationToken)
    {
        bool slotExists = await dbContext.StageSlots.AnyAsync(s => s.Id == request.StageSlotId, cancellationToken);
        if (!slotExists)
            return Result.Failure<ClearSlotResult>(StageErrors.SlotNotFound(request.StageSlotId));

        int total = await dbContext.CohortSlotAssignments
            .CountAsync(a => a.StageSlotId == request.StageSlotId, cancellationToken);

        var unpublishedAssignments = await dbContext.CohortSlotAssignments
            .Where(a => a.StageSlotId == request.StageSlotId
                     && !dbContext.ServicePeriodSlotCoverage.Any(c => c.CohortSlotAssignmentId == a.Id))
            .ToListAsync(cancellationToken);

        int cleared = unpublishedAssignments.Count;

        // Les deux nombres, jamais le seul « cleared » : à zéro il recouvre une colonne déjà vide et
        // une colonne entièrement publiée, qui appellent des actes opposés.
        auditTrail.RecordOutcome(
            ("cellsCleared", cleared),
            ("cellsKeptPublished", total - cleared),
            ("pinnedCellsCleared", unpublishedAssignments.Count(a => a.IsPinned)));

        dbContext.CohortSlotAssignments.RemoveRange(unpublishedAssignments);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClearSlotResult(cleared, total - cleared);
    }
}

internal sealed class CreateStageSlotCommandValidator : AbstractValidator<CreateStageSlotCommand>
{
    public CreateStageSlotCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
        RuleFor(x => x.PeriodNumber).GreaterThan(0);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after start date.");
        RuleFor(x => x.Label).MaximumLength(100).When(x => x.Label is not null);
    }
}

internal sealed class UpdateStageSlotCommandValidator : AbstractValidator<UpdateStageSlotCommand>
{
    public UpdateStageSlotCommandValidator()
    {
        RuleFor(x => x.SlotId).GreaterThan(0);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after start date.");
        RuleFor(x => x.Label).MaximumLength(100).When(x => x.Label is not null);
    }
}
