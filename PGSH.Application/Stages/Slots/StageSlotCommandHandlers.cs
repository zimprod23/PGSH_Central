using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
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

        var slot = new StageSlot
        {
            StageId        = request.StageId,
            AcademicYearId = request.AcademicYearId,
            PeriodNumber   = request.PeriodNumber,
            Label          = request.Label,
            StartDate      = request.StartDate,
            EndDate        = request.EndDate,
        };

        dbContext.StageSlots.Add(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return slot.Id;
    }
}

internal sealed class UpdateStageSlotCommandHandler(
    IApplicationDbContext dbContext,
    SlotOverlapGuard overlapGuard,
    GroupScheduleConflictGuard groupGuard)
    : ICommandHandler<UpdateStageSlotCommand>
{
    public async Task<Result> Handle(UpdateStageSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await dbContext.StageSlots
            .FirstOrDefaultAsync(s => s.Id == request.SlotId && s.StageId == request.StageId, cancellationToken);

        if (slot is null)
            return Result.Failure(StageErrors.SlotNotFound(request.SlotId));

        // Moving a period must respect the same rule as creating one — it is the same collision,
        // just reached by editing dates instead of adding a row.
        var overlap = await overlapGuard.EnsureNoOverlapAsync(
            request.StageId, slot.AcademicYearId, slot.PeriodNumber, request.StartDate, request.EndDate,
            excludedSlotId: slot.Id, cancellationToken);
        if (overlap.IsFailure)
            return overlap;

        // Dragging a period onto another stage's window double-books every group already in it,
        // without any cell being touched. The per-stage overlap check above cannot see that.
        var free = await groupGuard.EnsureSlotCanMoveAsync(
            slot.Id, request.StartDate, request.EndDate, cancellationToken);
        if (free.IsFailure)
            return free;

        slot.Label     = request.Label;
        slot.StartDate = request.StartDate;
        slot.EndDate   = request.EndDate;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeleteStageSlotCommandHandler(IApplicationDbContext dbContext)
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

        dbContext.StageSlots.Remove(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetCohortSlotAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    GroupScheduleConflictGuard groupGuard)
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

        bool serviceExists = await dbContext.Services.AnyAsync(s => s.Id == request.ServiceId, cancellationToken);
        if (!serviceExists)
            return Result.Failure<int>(Error.NotFound("Services.NotFound", $"Service {request.ServiceId} was not found."));

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

        bool isPublished = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .AnyAsync(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null), cancellationToken);

        if (isPublished)
            return Result.Failure<int>(StageErrors.ScheduleAlreadyPublished);

        var existing = await dbContext.CohortSlotAssignments
            .FirstOrDefaultAsync(a => a.CohortId == request.CohortId && a.StageSlotId == request.StageSlotId, cancellationToken);

        if (existing is not null)
        {
            // Only the service changes; the group already occupies this period legitimately, so
            // there is nothing new to conflict with.
            existing.ServiceId = request.ServiceId;
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
        };

        dbContext.CohortSlotAssignments.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return assignment.Id;
    }
}

internal sealed class ClearCohortSlotAssignmentCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<ClearCohortSlotAssignmentCommand>
{
    public async Task<Result> Handle(ClearCohortSlotAssignmentCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.CohortSlotAssignments
            .FirstOrDefaultAsync(a => a.CohortId == request.CohortId && a.StageSlotId == request.StageSlotId, cancellationToken);

        if (existing is null)
            return Result.Success();

        bool isPublished = await dbContext.IsCellPublishedAsync(existing.Id, cancellationToken);

        if (isPublished)
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        dbContext.CohortSlotAssignments.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ClearSlotAssignmentsCommandHandler(IApplicationDbContext dbContext)
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

        dbContext.CohortSlotAssignments.RemoveRange(unpublishedAssignments);
        await dbContext.SaveChangesAsync(cancellationToken);

        int cleared = unpublishedAssignments.Count;
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
