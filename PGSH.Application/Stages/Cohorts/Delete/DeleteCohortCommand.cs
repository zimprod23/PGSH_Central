using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.Delete;

/// <summary>
/// Removes one cohorte, with the affectations and périodes built on it.
/// </summary>
/// <remarks>
/// Refused once anything on it has begun, exactly as <c>DeleteAllCohortsCommand</c> is. There is no
/// force: the act that removes started périodes is « Dépublier », which names what it costs.
/// </remarks>
public sealed record DeleteCohortCommand(int CohortId)
    : ICommand<DeleteCohortResult>, IAuditableCommand
{
    public string AuditAction => "COHORT_DELETED";
    public string AuditEntityType => "Cohort";
    public string? AuditEntityId => CohortId.ToString();

    /// <summary>
    /// Rien de plus que la cible : ce qui compte ici est le <i>coût</i>, et le handler est le seul à
    /// le connaître. Voir <c>IAuditTrail</c>.
    /// </summary>
    public string? AuditMetadata => null;
}

/// <param name="AffectationsRemoved">Affectations deleted with the cohorte.</param>
/// <param name="PeriodsRemoved">
/// Périodes de service deleted with them — grid-linked <i>and</i> ad-hoc. Unlike unpublishing, which
/// leaves imported history and délocalisations alone, removing the cohorte removes the affectation
/// they hang off, so they go too. A destructive act that cannot name its count is one nobody can
/// consent to.
/// </param>
public sealed record DeleteCohortResult(int AffectationsRemoved, int PeriodsRemoved);
