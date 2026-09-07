using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Exports;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// Corrects a declared window — most often the day dates that were an estimate get settled, which is
/// why <paramref name="IsConfirmed"/> is editable rather than write-once.
/// </summary>
/// <remarks>
/// ⚠ <b>Correcting a window already begun is allowed, and it is the case that happens.</b> Declaring
/// pushed no date, so nothing double-counts when the window moves — which is exactly what
/// <c>InternshipAssignment.ResumePeriod</c> cannot say, and why that one cannot be corrected at all.
/// The promotion is <b>not</b> editable here: a window belongs to the promotion that declared it, and
/// moving it to another one is two acts (revoke, declare) with two confirmations.
/// </remarks>
public sealed record CorrectPromotionPauseCommand(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    PauseKind Kind,
    string Reason,
    bool IsConfirmed) : ICommand<PromotionPauseCorrectedResult>, IAuditableCommand
{
    public string AuditAction => "PROMOTION_PAUSE_CORRECTED";
    public string AuditEntityType => "PromotionPause";
    public string? AuditEntityId => Id.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("from", StartDate.ToString("yyyy-MM-dd")),
        ("to", EndDate.ToString("yyyy-MM-dd")),
        ("kind", Kind.ToString()),
        ("reason", Reason),
        ("confirmed", IsConfirmed));
}

internal sealed class CorrectPromotionPauseCommandValidator : AbstractValidator<CorrectPromotionPauseCommand>
{
    public CorrectPromotionPauseCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La suspension se termine avant de commencer.");
    }
}

internal sealed class CorrectPromotionPauseCommandHandler(
    IApplicationDbContext dbContext,
    PromotionPauseCalendarGuard calendarGuard,
    PromotionPauseImpactReader impactReader)
    : ICommandHandler<CorrectPromotionPauseCommand, PromotionPauseCorrectedResult>
{
    public async Task<Result<PromotionPauseCorrectedResult>> Handle(
        CorrectPromotionPauseCommand request, CancellationToken cancellationToken)
    {
        var pause = await dbContext.PromotionPauses
            .Include(p => p.AcademicYear)
            .Include(p => p.Level)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (pause is null)
            return Result.Failure<PromotionPauseCorrectedResult>(
                PromotionPauseErrors.NotFound(request.Id));

        string levelLabel = ExportLabels.Level(
            pause.Level.Label, pause.Level.Year, pause.Level.AcademicProgram);

        var free = await calendarGuard.EnsureFreeAsync(
            pause.AcademicYearId, pause.LevelId, levelLabel, request.StartDate, request.EndDate,
            excludingId: pause.Id, cancellationToken);

        if (free.IsFailure)
            return Result.Failure<PromotionPauseCorrectedResult>(free.Error);

        // ⚠ Counted before the write, and over the union of the span it leaves and the span it arrives
        // at. Both halves are affected and for opposite reasons: the first was measured against a
        // window that will no longer be there, the second has just gained one it never counted.
        // Overlapping spans — the usual case, a window corrected by a day — are counted once.
        var span = new PromotionPauseSpan(0, 0, 0);

        if (pause.WouldMove(request.StartDate, request.EndDate))
            span = await impactReader.SpanAsync(
                pause.AcademicYearId,
                pause.LevelId,
                pause.StartDate < request.StartDate ? pause.StartDate : request.StartDate,
                pause.EndDate > request.EndDate ? pause.EndDate : request.EndDate,
                cancellationToken);

        var corrected = pause.Correct(
            pause.AcademicYear, request.StartDate, request.EndDate, request.Kind, request.Reason,
            request.IsConfirmed);

        if (corrected.IsFailure)
            return Result.Failure<PromotionPauseCorrectedResult>(corrected.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PromotionPauseCorrectedResult(
            pause.Id,
            pause.Reason,
            pause.StartDate,
            pause.EndDate,
            corrected.Value.DatesMoved,
            span.SlotsSpanning,
            span.PeriodsSpanning,
            span.PeriodsUnderway);
    }
}
