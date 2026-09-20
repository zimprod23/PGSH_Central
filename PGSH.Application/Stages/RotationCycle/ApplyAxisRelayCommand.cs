using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// Repose l'axe d'une promotion sur son calendrier : les colonnes amputées reprennent leur compte de
/// jours ouvrables, et les rotations publiées suivent.
/// </summary>
/// <param name="ConfirmedSlotCount">
/// Ce que l'opérateur a vu dans la grille.
/// </param>
/// <param name="ConfirmedPeriodCount">
/// Ce que l'opérateur a vu dans les dossiers. ⚠ <b>Deux nombres, confirmés séparément, parce qu'ils
/// bougent pour des raisons différentes</b> : une rotation notée entre l'aperçu et l'application
/// change ce qui est écrit dans les dossiers sans rien changer à la grille. Un booléen ne peut pas
/// attraper cela, et un seul nombre en attraperait la moitié.
/// </param>
public sealed record ApplyAxisRelayCommand(
    int? LevelId,
    int ConfirmedSlotCount,
    int ConfirmedPeriodCount,
    int? AcademicYearId = null,
    int? FromPeriodNumber = null) : ICommand<AxisRelayResult>, IAuditableCommand
{
    public string AuditAction => "AXIS_RELAID";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId?.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        levelId = LevelId,
        academicYearId = AcademicYearId,
        fromPeriodNumber = FromPeriodNumber,
        confirmedSlots = ConfirmedSlotCount,
        confirmedPeriods = ConfirmedPeriodCount,
    });
}

internal sealed class ApplyAxisRelayCommandValidator : AbstractValidator<ApplyAxisRelayCommand>
{
    public ApplyAxisRelayCommandValidator()
    {
        // ⚠ Un acte qui détruit nomme son année, il ne la résout pas : « l'année en cours » est
        // précisément la promotion sur laquelle tout le monde travaille.
        RuleFor(x => x.LevelId)
            .NotNull().WithMessage("Précisez la promotion dont l'axe doit être recalculé.")
            .GreaterThan(0).WithMessage("La promotion est mal désignée.");

        RuleFor(x => x.AcademicYearId)
            .NotNull().WithMessage("Précisez l'année universitaire : un recalcul réécrit des dates.")
            .GreaterThan(0).WithMessage("L'année universitaire est mal désignée.");

        // Un client qui omet un compte confirmé est un client cassé, pas un utilisateur devant un
        // champ vide : ces deux-là restent obligatoires.
        RuleFor(x => x.ConfirmedSlotCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ConfirmedPeriodCount).GreaterThanOrEqualTo(0);
    }
}

/// <remarks>
/// <para>⚠ <b>Tout passe par les agrégats.</b> Les colonnes par <c>StageSlot.RelayTo</c> — qui refuse
/// une colonne déplacée à la main — et les rotations par <c>InternshipAssignment.Reschedule</c> ou
/// <c>ExtendTo</c>, qui portent la garde et lèvent l'événement. Écrire les dates à plat sur le
/// contexte marcherait et laisserait l'invariant à la charge de ce handler, ce qui est exactement ce
/// que la phase 17.1 a eu à défaire.</para>
///
/// <para>⚠ <b>Une seule transaction, par <c>IAuditTrail.RunAtomicallyAsync</c>.</b> ASP.NET annule le
/// jeton dès qu'un onglet se ferme, donc « la requête s'est arrêtée entre deux écritures » est le cas
/// ordinaire — et ici l'état partiel serait une grille et des dossiers qui ne disent plus la même
/// chose. Par la piste et non par le contexte : une nouvelle tentative vide le change tracker, et
/// seule la piste sait quelle version de son entrée est courante.</para>
///
/// <para>⚠ <b>Le rapport est <em>recalculé</em> dans la transaction, jamais repris de l'aperçu.</b>
/// C'est ce que les deux comptes confirmés vérifient : si quelqu'un a noté une rotation entre-temps,
/// le nombre a changé et l'acte refuse plutôt que d'écrire un plan qui n'est plus celui qu'on a
/// montré.</para>
/// </remarks>
internal sealed class ApplyAxisRelayCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    AxisRelayReader reader,
    IAuditTrail auditTrail,
    IDateTimeProvider clock)
    : ICommandHandler<ApplyAxisRelayCommand, AxisRelayResult>
{
    public async Task<Result<AxisRelayResult>> Handle(
        ApplyAxisRelayCommand request, CancellationToken cancellationToken) =>
        await auditTrail.RunAtomicallyAsync(ct => RelayAsync(request, ct), cancellationToken);

    private async Task<Result<AxisRelayResult>> RelayAsync(
        ApplyAxisRelayCommand request, CancellationToken ct)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<AxisRelayResult>(year.Error);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        int levelId = request.LevelId!.Value;

        var report = await reader.ReadAsync(
            year.Value, levelId, request.FromPeriodNumber, today, ct);

        if (report.IsFailure)
            return Result.Failure<AxisRelayResult>(report.Error);

        if (report.Value.SlotsToRelay != request.ConfirmedSlotCount)
            return Result.Failure<AxisRelayResult>(RotationCycleErrors.RelayCountMismatch(
                "colonnes", request.ConfirmedSlotCount, report.Value.SlotsToRelay));

        if (report.Value.PeriodsAffected != request.ConfirmedPeriodCount)
            return Result.Failure<AxisRelayResult>(RotationCycleErrors.RelayCountMismatch(
                "rotations", request.ConfirmedPeriodCount, report.Value.PeriodsAffected));

        var moved = report.Value.Columns
            .Where(c => c.Moved)
            .ToDictionary(c => c.Number, c => (c.ToStart, c.ToEnd));

        var slotsRelaid = await RelaySlotsAsync(year.Value, levelId, moved, ct);
        if (slotsRelaid.IsFailure)
            return Result.Failure<AxisRelayResult>(slotsRelaid.Error);

        var columns = report.Value.Columns
            .Select(c => new AxisColumn(c.Number, c.FromStart, c.FromEnd, c.Anchored))
            .ToList();

        var changes = await reader.PlanPeriodsAsync(year.Value, levelId, columns, moved, today, ct);

        var written = await ApplyToAssignmentsAsync(changes, today, ct);
        if (written.IsFailure)
            return Result.Failure<AxisRelayResult>(written.Error);

        // ⚠ Le code seul ne distingue pas « axe vierge repoussé » de « 4 010 rotations réécrites » :
        // le même handler produit les deux, et ce sont des événements sans rapport.
        auditTrail.RecordOutcome(
            ("fromPeriodNumber", report.Value.FromPeriodNumber),
            ("columnLength", report.Value.ColumnLength),
            ("slotsRelaid", slotsRelaid.Value),
            ("periodsMoved", written.Value.Moved),
            ("periodsExtended", written.Value.Extended),
            ("periodsShortened", written.Value.Shortened),
            ("periodsBlocked", report.Value.PeriodsBlocked),
            ("workingDaysChanged", report.Value.WorkingDaysChanged),
            ("axisEndsOn", report.Value.AxisEndsOn.ToString("yyyy-MM-dd")));

        await dbContext.SaveChangesAsync(ct);

        return new AxisRelayResult(
            slotsRelaid.Value,
            written.Value.Moved,
            written.Value.Extended,
            written.Value.Shortened,
            report.Value.PeriodsBlocked,
            report.Value.WorkingDaysChanged,
            report.Value.AxisEndsOn);
    }

    private async Task<Result<int>> RelaySlotsAsync(
        int academicYearId, int levelId,
        IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> moved,
        CancellationToken ct)
    {
        if (moved.Count == 0)
            return 0;

        var numbers = moved.Keys.ToList();

        var slots = await dbContext.StageSlots
            .Where(s => s.Stage.LevelId == levelId
                     && s.AcademicYearId == academicYearId
                     && numbers.Contains(s.PeriodNumber))
            .ToListAsync(ct);

        foreach (var slot in slots)
        {
            var window = moved[slot.PeriodNumber];

            // ⚠ RelayTo refuse une colonne déplacée à la main. Le rapport les a déjà écartées, donc
            // l'atteindre serait une incohérence — rendue plutôt qu'ignorée, et la transaction annule.
            var relaid = slot.RelayTo(window.Start, window.End);
            if (relaid.IsFailure)
                return Result.Failure<int>(relaid.Error);
        }

        return slots.Count;
    }

    /// <remarks>
    /// ⚠ <b>Les agrégats sont chargés avec de quoi juger.</b> <c>Evaluation</c> et <c>Attendance</c>
    /// sont ce que les gardes lisent, et une collection non incluse est indiscernable d'une collection
    /// vide — le fournisseur en mémoire la recolle depuis le change tracker et ne verrait jamais
    /// l'oubli, PostgreSQL la verrait comme « rien à signaler » et laisserait déplacer une rotation
    /// pointée.
    /// </remarks>
    private async Task<Result<(int Moved, int Extended, int Shortened)>> ApplyToAssignmentsAsync(
        IReadOnlyList<AxisRelayPeriodChange> changes, DateOnly on, CancellationToken ct)
    {
        var actionable = changes
            .Where(c => c.Action != AxisRelayAction.Blocked)
            .ToList();

        if (actionable.Count == 0)
            return (0, 0, 0);

        var assignmentIds = actionable.Select(c => c.InternshipAssignmentId).Distinct().ToList();

        var assignments = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Attendance)
            .Where(a => assignmentIds.Contains(a.Id))
            .ToListAsync(ct);

        var byId = assignments.ToDictionary(a => a.Id);
        int moved = 0, extended = 0, shortened = 0;

        foreach (var change in actionable)
        {
            if (!byId.TryGetValue(change.InternshipAssignmentId, out var assignment))
                return Result.Failure<(int, int, int)>(
                    StageErrors.PeriodNotFound(change.ServicePeriodId));

            // ⚠ Trois portes, trois gardes. Le rapport a déjà choisi laquelle ; l'agrégat la
            // re-vérifie, parce qu'un invariant que l'appelant garantit n'en est pas un.
            var applied = change.Action switch
            {
                AxisRelayAction.Move =>
                    assignment.Reschedule(change.ServicePeriodId, change.ToStart, change.ToEnd, on),
                AxisRelayAction.Extend =>
                    assignment.ExtendTo(change.ServicePeriodId, change.ToEnd),
                _ =>
                    assignment.ShortenTo(change.ServicePeriodId, change.ToEnd),
            };

            if (applied.IsFailure)
                return Result.Failure<(int, int, int)>(applied.Error);

            switch (change.Action)
            {
                case AxisRelayAction.Move: moved++; break;
                case AxisRelayAction.Extend: extended++; break;
                default: shortened++; break;
            }
        }

        return (moved, extended, shortened);
    }
}
