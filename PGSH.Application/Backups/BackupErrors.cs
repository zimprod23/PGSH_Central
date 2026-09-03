using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

public static class BackupErrors
{
    public static Error NotFound(string id) =>
        Error.NotFound("Backups.NotFound", $"Point de sauvegarde « {id} » introuvable.");

    /// <summary>
    /// ⚠ Carries the runner's own sentence. « La sauvegarde a échoué » sends the operator hunting; the
    /// reason is nearly always one line — Docker not running, the container renamed, the directory not
    /// writable — and it is the line that says which.
    /// </summary>
    public static Error Unavailable(string reason) => Error.Problem(
        "Backups.Unavailable",
        $"Le service de sauvegarde est indisponible : {reason}");

    public static Error DumpFailed(string reason) => Error.Problem(
        "Backups.DumpFailed",
        $"pg_dump a échoué : {reason}");

    public static Error VerificationFailed(string id, string reason) => Error.Problem(
        "Backups.VerificationFailed",
        $"L'archive « {id} » n'a pas pu être relue : {reason}");

    /// <summary>
    /// The newest point is the one every confirmation dialog is reading; removing it silently moves
    /// every bulk act onto an older undo, or onto none. Delete an older one, or take a new point first.
    /// </summary>
    public static Error CannotDeleteLatest(string id) => Error.Conflict(
        "Backups.CannotDeleteLatest",
        $"« {id} » est le point de sauvegarde le plus récent : c'est celui sur lequel s'appuient les "
        + "confirmations des actes en masse. Supprimez-en un plus ancien, ou créez un nouveau point d'abord.");

    public static Error NotAllowed => Error.Forbidden(
        "Backups.NotAllowed",
        "Seule l'administration peut gérer les sauvegardes.");
}
