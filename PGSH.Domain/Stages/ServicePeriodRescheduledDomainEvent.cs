using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// La fenêtre d'une rotation <b>publiée</b> a été déplacée — phase 17.1, quand on bouge une colonne de
/// la grille avec les périodes qui en viennent.
/// </summary>
/// <remarks>
/// ⚠ <b>C'est un fait sur le dossier de l'étudiant, pas un détail de planification.</b> Les dates
/// d'avant n'existent plus nulle part une fois la ligne écrite : le registre les garde
/// (<c>STAGE_SLOT_UPDATED</c> porte <c>periodsCovered</c> et <c>periodsShifted</c>), mais le registre
/// répond à « qui a fait quoi », et cet événement répond à « qu'est-il arrivé à ce stage-là ». Un acte
/// qui déplace des milliers de rotations d'un coup en levait <b>zéro</b>, là où bien plus petit que lui
/// en lève un.
///
/// <para>⚠ <b>Il porte les deux fenêtres.</b> Un événement qui ne dirait que la nouvelle laisserait un
/// abonné incapable de dire de combien la rotation a bougé, ni dans quel sens — et « avancée d'une
/// semaine » et « repoussée d'un mois » n'appellent pas les mêmes suites.</para>
/// </remarks>
public sealed record ServicePeriodRescheduledDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    Guid ServicePeriodId,
    DateOnly FromStartDate,
    DateOnly FromEndDate,
    DateOnly ToStartDate,
    DateOnly ToEndDate) : IDomainEvent;
