using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

public static class PartitionErrors
{
    public static Error CannotReassignPublished(int publishedCells) => Error.Conflict(
        "Partitions.CannotReassignPublished",
        $"{publishedCells} créneau(x) de cette promotion sont déjà publiés — des étudiants y ont été "
        + "envoyés. Redécouper les partitions laisserait le planning publié décrire un découpage qui "
        + "n'existe plus.");
}
