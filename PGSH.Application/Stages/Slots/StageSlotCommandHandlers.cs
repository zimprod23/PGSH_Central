using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

internal sealed class CreateStageSlotCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateStageSlotCommand, int>
{
    public async Task<Result<int>> Handle(CreateStageSlotCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages.AnyAsync(s => s.Id == request.StageId, cancellationToken);
        if (!stageExists)
            return Result.Failure<int>(StageErrors.NotFound(request.StageId));

        bool duplicate = await dbContext.StageSlots
            .AnyAsync(s => s.StageId == request.StageId && s.PeriodNumber == request.PeriodNumber, cancellationToken);
        if (duplicate)
            return Result.Failure<int>(StageErrors.DuplicatePeriodNumber(request.PeriodNumber));

        var slot = new StageSlot
        {
            StageId      = request.StageId,
            PeriodNumber = request.PeriodNumber,
            Label        = request.Label,
            StartDate    = request.StartDate,
            EndDate      = request.EndDate,
        };

        dbContext.StageSlots.Add(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return slot.Id;
    }
}

internal sealed class UpdateStageSlotCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateStageSlotCommand>
{
    public async Task<Result> Handle(UpdateStageSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await dbContext.StageSlots
            .FirstOrDefaultAsync(s => s.Id == request.SlotId && s.StageId == request.StageId, cancellationToken);

        if (slot is null)
            return Result.Failure(StageErrors.SlotNotFound(request.SlotId));

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

        dbContext.StageSlots.Remove(slot);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetCohortSlotAssignmentCommandHandler(IApplicationDbContext dbContext)
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
            existing.ServiceId = request.ServiceId;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

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

        bool isPublished = await dbContext.InternshipAssignments
            .Where(a => a.CurrentCohortId == request.CohortId)
            .AnyAsync(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId == existing.Id), cancellationToken);

        if (isPublished)
            return Result.Failure(StageErrors.ScheduleAlreadyPublished);

        dbContext.CohortSlotAssignments.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CreateStageSlotCommandValidator : AbstractValidator<CreateStageSlotCommand>
{
    public CreateStageSlotCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
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
