using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Calendar;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// What declaring this window would cost the promotion. Writes nothing, and the numbers it returns are
/// the ones the declaration reports — same reader, so the dry run is the act.
/// </summary>
/// <param name="ExcludingPauseId">
/// Set when previewing a <b>correction</b>: the window already recorded is left out of the calendar, so
/// the report measures the new dates rather than measuring them against themselves.
/// </param>
public sealed record PreviewPromotionPauseQuery(
    int LevelId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason = "",
    bool IsConfirmed = true,
    int? AcademicYearId = null,
    int? ExcludingPauseId = null) : IQuery<PromotionPauseImpactResponse>;

internal sealed class PreviewPromotionPauseQueryValidator : AbstractValidator<PreviewPromotionPauseQuery>
{
    public PreviewPromotionPauseQueryValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La suspension se termine avant de commencer.");
    }
}

internal sealed class PreviewPromotionPauseQueryHandler(
    PromotionPauseContext context,
    PromotionPauseImpactReader impactReader)
    : IQueryHandler<PreviewPromotionPauseQuery, PromotionPauseImpactResponse>
{
    public async Task<Result<PromotionPauseImpactResponse>> Handle(
        PreviewPromotionPauseQuery request, CancellationToken cancellationToken)
    {
        var promotion = await context.ResolveAsync(request.LevelId, request.AcademicYearId, cancellationToken);
        if (promotion.IsFailure)
            return Result.Failure<PromotionPauseImpactResponse>(promotion.Error);

        // ⚠ The same refusal the declaration gives, on the same rows — a preview that reported an
        // impact for a window the act would refuse is a preview of nothing. « Retrait » sits no exams.
        if (!promotion.Value.Level.IsPromotion)
            return Result.Failure<PromotionPauseImpactResponse>(
                LevelErrors.NotAPromotion(promotion.Value.LevelLabel));

        return await impactReader.MeasureAsync(
            promotion.Value,
            request.StartDate,
            request.EndDate,
            string.IsNullOrWhiteSpace(request.Reason) ? "Suspension" : request.Reason.Trim(),
            request.IsConfirmed,
            request.ExcludingPauseId,
            cancellationToken);
    }
}
