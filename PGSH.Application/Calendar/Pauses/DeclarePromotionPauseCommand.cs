using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Calendar;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// Declares a window during which one promotion is not in its services — an exam session, most often.
/// </summary>
/// <remarks>
/// ⚠ <b>The widest act in the planning area, and it writes exactly one row.</b> It changes what every
/// stage of a promotion is measured against, and it does so by joining that promotion's working-day
/// calendar rather than by pushing dates on anything — see <see cref="PromotionPause"/> for why that is
/// the whole design and not a shortcut. The counts it returns are what the plan already laid loses, so
/// the faculty can decide whether to re-lay the axis.
/// </remarks>
public sealed record DeclarePromotionPauseCommand(
    int LevelId,
    DateOnly StartDate,
    DateOnly EndDate,
    PauseKind Kind,
    string Reason,
    bool IsConfirmed = true,
    int? AcademicYearId = null) : ICommand<PromotionPauseDeclaredResult>, IAuditableCommand
{
    public string AuditAction => "PROMOTION_PAUSE_DECLARED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("academicYearId", AcademicYearId),
        ("from", StartDate.ToString("yyyy-MM-dd")),
        ("to", EndDate.ToString("yyyy-MM-dd")),
        ("kind", Kind.ToString()),
        ("reason", Reason),
        ("confirmed", IsConfirmed));
}

internal sealed class DeclarePromotionPauseCommandValidator : AbstractValidator<DeclarePromotionPauseCommand>
{
    public DeclarePromotionPauseCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La suspension se termine avant de commencer.");
    }
}

internal sealed class DeclarePromotionPauseCommandHandler(
    IApplicationDbContext dbContext,
    PromotionPauseContext context,
    PromotionPauseCalendarGuard calendarGuard,
    PromotionPauseImpactReader impactReader,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<DeclarePromotionPauseCommand, PromotionPauseDeclaredResult>
{
    public async Task<Result<PromotionPauseDeclaredResult>> Handle(
        DeclarePromotionPauseCommand request, CancellationToken cancellationToken)
    {
        var promotion = await context.ResolveAsync(request.LevelId, request.AcademicYearId, cancellationToken);
        if (promotion.IsFailure)
            return Result.Failure<PromotionPauseDeclaredResult>(promotion.Error);

        var (year, level, levelLabel) = promotion.Value;

        var free = await calendarGuard.EnsureFreeAsync(
            year.Id, level.Id, levelLabel, request.StartDate, request.EndDate,
            excludingId: null, cancellationToken);

        if (free.IsFailure)
            return Result.Failure<PromotionPauseDeclaredResult>(free.Error);

        var declared = PromotionPause.Declare(
            level, year, request.StartDate, request.EndDate, request.Kind, request.Reason,
            request.IsConfirmed, dateTimeProvider.UtcNow);

        if (declared.IsFailure)
            return Result.Failure<PromotionPauseDeclaredResult>(declared.Error);

        // ⚠ Measured before the row is saved, and against a calendar that does not yet contain it:
        // asked afterwards, a window costs zero worked days by construction.
        var impact = await impactReader.MeasureAsync(
            promotion.Value, request.StartDate, request.EndDate, request.Reason, request.IsConfirmed,
            excludingPauseId: null, cancellationToken);

        dbContext.PromotionPauses.Add(declared.Value);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PromotionPauseDeclaredResult(
            declared.Value.Id,
            declared.Value.StartDate,
            declared.Value.EndDate,
            impact.WorkingDaysLost,
            impact.SlotsSpanning,
            impact.PeriodsSpanning,
            impact.PeriodsUnderway);
    }
}
