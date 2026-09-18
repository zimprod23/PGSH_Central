using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// « Cette promotion est-elle en examens aujourd'hui, et sous quel motif ? »
/// </summary>
/// <remarks>
/// <para><b>Le pendant lisible d'une fenêtre qui n'écrit rien.</b> Déclarer une suspension ne touche
/// aucune rotation — c'est ce qui la rend révocable — donc la seule façon dont elle peut se voir est
/// d'être <b>dérivée</b> à la lecture. Sans cela une promotion entière reste « En cours » pendant ses
/// examens : mesuré sur la base vivante le 18/09/2026, <b>472 rotations</b> se lisaient « en service »
/// un matin où la fenêtre déclarée disait le contraire, et rien nulle part ne le contredisait.</para>
///
/// <para>⚠ <b>Dérivé, jamais stocké, et c'est la leçon de l'acte retiré la veille.</b> Un drapeau posé
/// sur des milliers de périodes doit être enlevé par quelqu'un, s'accumule si on rejoue, et survit à la
/// révocation de la fenêtre qui l'a causé. Ici il n'y a rien à défaire : révoquer la fenêtre fait
/// disparaître l'état à la lecture suivante, pour les 472 d'un coup et sans un seul écrit.</para>
///
/// <para>⚠ <b>La date entre, elle ne se lit pas ici.</b> <c>DateTime.UtcNow</c> pris au fond d'une
/// classe est précisément ce qui rendait l'ancienne pause impossible à programmer et impossible à
/// tester ; l'appelant passe <see cref="IDateTimeProvider"/>.</para>
///
/// <para>⚠ <b>La promotion est (année, niveau) et se lit sur l'<em>inscription</em>.</b> Un sixième
/// année qui refait un stage de troisième passe les examens de <i>sa</i> promotion, pas ceux du stage —
/// même choix que <c>PromotionPauseQueries.PeriodsQuery</c>, et les deux ne doivent pas diverger.</para>
/// </remarks>
internal sealed class PromotionSuspensionLookup(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Les fenêtres couvrant <paramref name="on"/>, indexées par promotion.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Une requête pour toute la page, jamais une par ligne.</b> Deux fenêtres d'une même
    /// promotion ne peuvent pas se chevaucher (<c>PromotionPauseCalendarGuard</c>), donc une promotion
    /// a au plus une fenêtre à une date donnée et le dictionnaire est exact — ce n'est pas un premier
    /// arrivé arbitraire.
    /// </remarks>
    public async Task<IReadOnlyDictionary<(int AcademicYearId, int LevelId), PromotionSuspension>> OnAsync(
        DateOnly on, CancellationToken cancellationToken)
    {
        var rows = await dbContext.PromotionPauses
            .AsNoTracking()
            .Where(w => w.StartDate <= on && w.EndDate >= on)
            .Select(w => new
            {
                w.Id, w.AcademicYearId, w.LevelId, w.Kind, w.Reason,
                w.StartDate, w.EndDate, w.IsConfirmed,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            w => (w.AcademicYearId, w.LevelId),
            w => new PromotionSuspension(
                w.Id, w.Kind, w.Reason, w.StartDate, w.EndDate, w.IsConfirmed));
    }
}

/// <summary>
/// Ce qui suspend une promotion à une date : le motif que la faculté a écrit, et jusqu'à quand.
/// </summary>
/// <param name="Reason">
/// Le motif saisi à la déclaration — « Examens du 1er semestre ». ⚠ C'est <b>lui</b> qui s'affiche à la
/// place du statut : « En cours » est vrai du cycle de vie de la rotation et faux de l'endroit où
/// l'étudiant se trouve ce matin, et c'est la seconde question que l'écran pose.
/// </param>
/// <param name="EndDate">
/// Jusqu'à quand, parce qu'un état sans terme se lit comme un blocage. L'étudiant redevient « En
/// cours » le lendemain, tout seul, sans qu'aucun acte soit joué.
/// </param>
/// <param name="IsConfirmed">
/// Faux quand les dates sont encore provisoires. La fenêtre compte quand même — elle peut seulement
/// encore bouger — donc l'écran le dit au lieu de la cacher ou de la présenter comme acquise.
/// </param>
public sealed record PromotionSuspension(
    int PauseId,
    PauseKind Kind,
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsConfirmed);
