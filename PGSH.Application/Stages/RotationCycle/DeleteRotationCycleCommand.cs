using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Removes a block's axis: every slot its stages hold for the year, and with them the cells that were
/// planned on those slots.
/// </summary>
/// <remarks>
/// <para><b>Why it is an act of its own.</b> The apply replaces an axis, so a block entered by mistake
/// could only be "removed" by authoring a different one over it — there was no way back to a promotion
/// with no block at all, short of deleting the slots one at a time from each stage's own grid. Same
/// shape as <c>ClearRotationGroupsCommand</c>: replacing is not undoing, and the undo needs its own
/// button.</para>
///
/// <para>⚠ <b>Scoped to the stages named, never to the level.</b> One promotion legitimately holds
/// several blocks — the new CNPN's 3ᵉ année is two blocks of three stages, a semester each — so
/// "remove this level's axis" would take the other semester with it. The caller names the block it is
/// looking at, exactly as the apply does.</para>
///
/// <para><b>Refused once anything on it is published</b>, for the reason the apply is refused: students
/// were sent to those windows, and a published plan describing windows that no longer exist is not a
/// record of anything. Unpublish first — that path exists, is guarded, and says what it would cost.</para>
/// </remarks>
public sealed record DeleteRotationCycleCommand(
    int LevelId,
    IReadOnlyList<int> StageIds,
    int? AcademicYearId = null) : ICommand<DeleteRotationCycleResult>, IAuditableCommand
{
    public string AuditAction => "ROTATION_CYCLE_DELETED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        levelId = LevelId,
        academicYearId = AcademicYearId,
        stageIds = StageIds,
    });
}

/// <param name="PlannedCellsRemoved">
/// Cells that hung off the removed slots. They cascade, and unlike the apply's there is no matrix left
/// to rebuild them from — which is precisely why the count is returned rather than assumed harmless.
/// </param>
public sealed record DeleteRotationCycleResult(int SlotsRemoved, int PlannedCellsRemoved);

internal sealed class DeleteRotationCycleCommandValidator : AbstractValidator<DeleteRotationCycleCommand>
{
    public DeleteRotationCycleCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.StageIds).NotEmpty().WithMessage("Indiquez les stages du bloc à supprimer.");
    }
}

internal sealed class DeleteRotationCycleCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    RotationCycleContext context)
    : ICommandHandler<DeleteRotationCycleCommand, DeleteRotationCycleResult>
{
    public async Task<Result<DeleteRotationCycleResult>> Handle(
        DeleteRotationCycleCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<DeleteRotationCycleResult>(year.Error);

        int yearId = year.Value;

        // The same resolution the preview and the apply use: it checks the stages exist and belong to
        // this level, which is what stops a removal reaching another promotion's axis.
        var resolved = await context.ResolveAsync(request.LevelId, request.StageIds, yearId, cancellationToken);
        if (resolved.IsFailure)
            return Result.Failure<DeleteRotationCycleResult>(resolved.Error);

        if (resolved.Value.PublishedCells > 0)
            return Result.Failure<DeleteRotationCycleResult>(
                RotationCycleErrors.CannotDeletePublished(resolved.Value.PublishedCells));

        var slots = await dbContext.StageSlots
            .Where(s => request.StageIds.Contains(s.StageId) && s.AcademicYearId == yearId)
            .ToListAsync(cancellationToken);

        if (slots.Count == 0)
            return Result.Failure<DeleteRotationCycleResult>(RotationCycleErrors.NoBlockToDelete);

        dbContext.StageSlots.RemoveRange(slots);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteRotationCycleResult(slots.Count, resolved.Value.PlannedCells);
    }
}
