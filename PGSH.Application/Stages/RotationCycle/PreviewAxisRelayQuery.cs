using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Extensions;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Ce qu'un recalcul d'axe ferait de cette promotion, sans rien écrire.
/// </summary>
/// <param name="FromPeriodNumber">
/// La colonne par laquelle commencer. Omise, l'aperçu prend la première que le calendrier ampute —
/// ce qui est la réponse voulue dans le cas courant, et la nommer reste possible quand un opérateur
/// veut épargner une colonne en cours.
/// </param>
public sealed record PreviewAxisRelayQuery(
    int? LevelId,
    int? AcademicYearId = null,
    int? FromPeriodNumber = null) : IQuery<AxisRelayPreviewResponse>;

internal sealed class PreviewAxisRelayQueryValidator : AbstractValidator<PreviewAxisRelayQuery>
{
    public PreviewAxisRelayQueryValidator()
    {
        // ⚠ Lié en nullable et refusé en toutes lettres : un type valeur obligatoire en query string
        // lève avant le pipeline, et l'écran ne reçoit qu'un 400 nu qu'il rend en « Données invalides ».
        RuleFor(x => x.LevelId).IsARequiredReference(
            "Précisez la promotion dont l'axe doit être recalculé.");

        RuleFor(x => x.FromPeriodNumber)
            .GreaterThan(0)
            .When(x => x.FromPeriodNumber is not null)
            .WithMessage("Une colonne est numérotée à partir de 1.");
    }
}

internal sealed class PreviewAxisRelayQueryHandler(
    AcademicYearResolver yearResolver,
    AxisRelayReader reader,
    IDateTimeProvider clock)
    : IQueryHandler<PreviewAxisRelayQuery, AxisRelayPreviewResponse>
{
    public async Task<Result<AxisRelayPreviewResponse>> Handle(
        PreviewAxisRelayQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<AxisRelayPreviewResponse>(year.Error);

        var report = await reader.ReadAsync(
            year.Value, request.LevelId!.Value, request.FromPeriodNumber,
            DateOnly.FromDateTime(clock.UtcNow), cancellationToken);

        return report.IsFailure
            ? Result.Failure<AxisRelayPreviewResponse>(report.Error)
            : AxisRelayPresentation.ToPreview(report.Value);
    }
}

/// <summary>
/// La seule traduction du rapport interne vers la réponse publique — partagée par l'aperçu et par
/// l'application, pour que les deux ne puissent pas compter différemment.
/// </summary>
internal static class AxisRelayPresentation
{
    public static AxisRelayPreviewResponse ToPreview(AxisRelayReport report) =>
        new(report.AcademicYearId,
            report.LevelId,
            report.ColumnLength,
            report.ColumnsAgreeingOnLength,
            report.ColumnCount,
            report.FromPeriodNumber,
            report.Columns
                .Select(c => new AxisRelayColumnResponse(
                    c.Number,
                    c.FromStart, c.FromEnd,
                    c.ToStart, c.ToEnd,
                    c.FromWorkingDays, c.ToWorkingDays,
                    c.Anchored,
                    c.Moved))
                .ToList(),
            report.Columns.Count(c => c.Moved),
            report.Columns.Count(c => c.Anchored),
            report.SlotsToRelay,
            report.PeriodsToMove,
            report.PeriodsToExtend,
            report.PeriodsBlocked,
            report.WorkingDaysRecovered,
            report.AxisEndsOn,
            report.Warnings);
}
