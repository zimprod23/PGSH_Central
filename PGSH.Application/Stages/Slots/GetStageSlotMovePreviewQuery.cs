using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Slots;

/// <summary>
/// « Si je déplace cette colonne ici, qu'est-ce qui bouge ? » — l'aperçu que
/// <c>UpdateStageSlotCommand</c> fait confirmer.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Il rejoue le même planificateur que l'acte</b> (<see cref="PublishedPeriodShifter"/>),
/// il ne recompte pas de son côté. Deux arithmétiques pour un seul geste finissent par diverger, et
/// c'est le nombre affiché qui sert ensuite de garde : s'il vient d'ailleurs que de l'acte, la garde
/// compare deux choses différentes et ne protège rien.</para>
///
/// <para>⚠ <b>Un refus est une réponse, pas une erreur d'aperçu.</b> Une colonne dont les périodes
/// ont commencé ne peut pas bouger, et l'écran doit pouvoir le <i>dire</i> avant que l'opérateur
/// remplisse un formulaire — d'où <see cref="StageSlotMovePreview.RefusalMessage"/> plutôt qu'un 409
/// sur une simple lecture.</para>
/// </remarks>
public sealed record GetStageSlotMovePreviewQuery(
    int       SlotId,
    DateOnly? StartDate,
    DateOnly? EndDate) : IQuery<StageSlotMovePreview>;

/// <param name="PeriodsCovered">
/// Périodes publiées que cette colonne touche. ⚠ C'est <b>ce nombre</b> que l'acte fait confirmer,
/// pas celui d'en dessous : c'est lui qui dit l'ampleur de ce qui est réécrit.
/// </param>
/// <param name="PeriodsWhoseWindowMoves">
/// Combien verraient leurs dates changer. ⚠ Plus petit que le précédent dès qu'un stage en service
/// unique est concerné : déplacer une colonne du milieu d'un séjour n'en change pas la durée. Les
/// confondre annoncerait des milliers de rotations réécrites là où aucune ne l'est.
/// </param>
/// <param name="RefusalMessage">
/// Pourquoi le déplacement serait refusé, en toutes lettres, ou <c>null</c> s'il passerait.
/// </param>
public sealed record StageSlotMovePreview(
    int       SlotId,
    int       PeriodNumber,
    DateOnly  CurrentStartDate,
    DateOnly  CurrentEndDate,
    DateOnly  ProposedStartDate,
    DateOnly  ProposedEndDate,
    int       PeriodsCovered,
    int       PeriodsWhoseWindowMoves,
    string?   RefusalMessage);

internal sealed class GetStageSlotMovePreviewQueryValidator
    : AbstractValidator<GetStageSlotMovePreviewQuery>
{
    public GetStageSlotMovePreviewQueryValidator()
    {
        RuleFor(x => x.StartDate).IsARequiredDate(
            "La date de début est obligatoire : l'aperçu répond à « si je déplace cette colonne "
            + "*ici* », et sans fenêtre il n'y a rien à simuler.");

        RuleFor(x => x.EndDate).IsARequiredDate(
            "La date de fin est obligatoire : l'aperçu répond à « si je déplace cette colonne "
            + "*ici* », et sans fenêtre il n'y a rien à simuler.");

        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.EndDate is null || x.EndDate >= x.StartDate)
            .WithMessage("La fin d'une colonne ne peut pas précéder son début.");
    }
}

internal sealed class GetStageSlotMovePreviewQueryHandler(
    IApplicationDbContext dbContext,
    PublishedPeriodShifter shifter)
    : IQueryHandler<GetStageSlotMovePreviewQuery, StageSlotMovePreview>
{
    public async Task<Result<StageSlotMovePreview>> Handle(
        GetStageSlotMovePreviewQuery request, CancellationToken cancellationToken)
    {
        // Nullable seulement pour que l'omission arrive jusqu'au validateur au lieu de lever dans le
        // routage ; ici les deux ont été refusées en toutes lettres si elles manquaient.
        DateOnly start = request.StartDate!.Value;
        DateOnly end   = request.EndDate!.Value;

        var slot = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.Id == request.SlotId)
            .Select(s => new { s.Id, s.PeriodNumber, s.StartDate, s.EndDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
            return Result.Failure<StageSlotMovePreview>(StageErrors.SlotNotFound(request.SlotId));

        var plan = await shifter.PlanAsync(slot.Id, start, end, cancellationToken);

        return new StageSlotMovePreview(
            slot.Id,
            slot.PeriodNumber,
            slot.StartDate,
            slot.EndDate,
            start,
            end,
            plan.IsSuccess ? plan.Value.PeriodsAffected : 0,
            plan.IsSuccess ? plan.Value.PeriodsWhoseWindowChanges : 0,
            plan.IsSuccess ? null : plan.Error.Description);
    }
}
