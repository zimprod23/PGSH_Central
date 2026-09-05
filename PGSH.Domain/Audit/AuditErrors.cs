using PGSH.SharedKernel;

namespace PGSH.Domain.Audit;

public static class AuditErrors
{
    /// <summary>
    /// Le journal couvre les actes de toute la faculté — découpage de promotions, verdicts d'année,
    /// suppressions. C'est une lecture d'administration, au même titre que la déliberation.
    /// </summary>
    public static readonly Error NotAllowed = Error.Forbidden(
        "Audit.NotAllowed",
        "Seule la scolarité peut consulter le journal des actions.");
}
