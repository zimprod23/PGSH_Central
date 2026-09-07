using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// Withdraws a declared window: the promotion is back in its services on those days.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Prospective, and stated rather than inherited.</b> Revoking a window that has already
/// begun is allowed and changes nothing about what happened: declaring pushed no date, so there is no
/// compensation to walk back. Créneaux laid while it stood keep the dates they were given, and périodes
/// already served keep what actually happened. What the removal changes is every measurement from now
/// on — the days go back into the promotion's calendar. Same rule as
/// <c>CnpnVersion.WithdrawEffectivity</c> and <c>DeleteHolidayCommand</c>, which is why the result
/// reports how many créneaux were laid across it.</para>
///
/// <para>⚠ <b>No domain event.</b> The aggregate root is removed, and EF detaches a deleted entity
/// before <c>ApplicationDbContext</c> collects events from the change tracker — one raised here would
/// be dropped without a trace. The register records the act instead.</para>
/// </remarks>
public sealed record RevokePromotionPauseCommand(int Id)
    : ICommand<PromotionPauseRevokedResult>, IAuditableCommand
{
    public string AuditAction => "PROMOTION_PAUSE_REVOKED";
    public string AuditEntityType => "PromotionPause";
    public string? AuditEntityId => Id.ToString();
    public string? AuditMetadata => null;
}

internal sealed class RevokePromotionPauseCommandValidator : AbstractValidator<RevokePromotionPauseCommand>
{
    public RevokePromotionPauseCommandValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

internal sealed class RevokePromotionPauseCommandHandler(
    IApplicationDbContext dbContext,
    PromotionPauseImpactReader impactReader,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<RevokePromotionPauseCommand, PromotionPauseRevokedResult>
{
    public async Task<Result<PromotionPauseRevokedResult>> Handle(
        RevokePromotionPauseCommand request, CancellationToken cancellationToken)
    {
        var pause = await dbContext.PromotionPauses
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (pause is null)
            return Result.Failure<PromotionPauseRevokedResult>(
                PromotionPauseErrors.NotFound(request.Id));

        // Counted before the row goes, for the same reason DeleteHolidayCommand counts before it
        // removes: afterwards there is nothing left to measure the span against.
        var span = await impactReader.SpanAsync(
            pause.AcademicYearId, pause.LevelId, pause.StartDate, pause.EndDate, cancellationToken);

        var revoked = new PromotionPauseRevokedResult(
            pause.Reason,
            pause.StartDate,
            pause.EndDate,
            pause.HasBegun(DateOnly.FromDateTime(dateTimeProvider.UtcNow)),
            span.SlotsSpanning,
            span.PeriodsSpanning,
            span.PeriodsUnderway);

        dbContext.PromotionPauses.Remove(pause);
        await dbContext.SaveChangesAsync(cancellationToken);

        return revoked;
    }
}
