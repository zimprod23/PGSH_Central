using PGSH.SharedKernel;

namespace PGSH.Domain.Hospitals;

public static class ServiceErrors
{
    public static Error NotFound(int id) => Error.NotFound(
        "Services.NotFound", $"Service {id} not found.");

    public static Error DuplicateName => Error.Conflict(
        "Services.DuplicateName", "A service with this name already exists in this hospital.");

    /// <summary>
    /// Le service est encore référencé, et par quoi.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Les raisons sont comptées ensemble, jamais court-circuitées à la première.</b>
    /// Même règle et même forme que <c>StageErrors.StillInUse</c> et
    /// <c>AcademicYearErrors.StillInUse</c> : un utilisateur qui retire le service d'un stage pour
    /// s'entendre dire ensuite qu'il porte des cellules a fait le tour deux fois, et le second tour
    /// ressemble à un premier correctif qui aurait échoué.</para>
    ///
    /// <para>⚠ <b>Le conseil final dépend de <paramref name="holdsHistory"/>, parce que les deux
    /// situations n'ont pas la même issue.</b> Des cellules et des autorisations se retirent ; des
    /// périodes déjà enregistrées, non — elles font partie du dossier des étudiants, et
    /// « retirez ces rattachements d'abord » enverrait alors l'opérateur chercher une manœuvre qui
    /// n'existe pas. Sur cette base, les 105 000 périodes reprises de l'Access suffisent à rendre la
    /// plupart des services indélébiles, donc c'est le cas ordinaire et non le cas rare.</para>
    /// </remarks>
    public static Error StillInUse(string serviceName, IReadOnlyList<string> holdings, bool holdsHistory) =>
        Error.Conflict(
            "Services.StillInUse",
            $"« {serviceName} » ne peut pas être supprimé : il est encore référencé par "
            + string.Join(", ", holdings)
            + (holdsHistory
                ? ". Les périodes déjà enregistrées font partie du dossier des étudiants et ne se "
                  + "retirent pas : retirez le service des listes de services autorisés pour qu'aucun "
                  + "stage ne l'utilise plus."
                : ". Retirez ces rattachements d'abord."));

    // === Intake rules (ServiceLevelCapacity) ===

    public static Error UnknownLevel(int levelId) => Error.NotFound(
        "Services.UnknownLevel",
        $"Le niveau {levelId} n'existe pas — impossible de lui accorder un quota.");

    public static Error DuplicateLevelQuota(int levelId) => Error.Conflict(
        "Services.DuplicateLevelQuota",
        $"Le niveau {levelId} apparaît deux fois dans les quotas : une promotion ne peut avoir qu'un seul quota par service.");

    // No "quota exceeds the service's capacity" rule: quotas replace Service.Capacity rather than
    // sitting under it, so on a restricted service that number governs nothing and a quota above it
    // contradicts nothing. See ServiceLevelCapacity.
}
