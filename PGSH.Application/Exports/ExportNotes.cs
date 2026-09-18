using PGSH.Application.Hospitals.Chefs;

namespace PGSH.Application.Exports;

/// <summary>
/// What the document has to say about itself beyond its rows.
///
/// <para><b>Why this exists.</b> The roll of 2026-2027 came out with `Groupe`, `N° groupe`,
/// `Partition`, `Source de la décision` and `Convention` blank on all 5 932 lines — and every one of
/// those blanks was correct: no inscription carries a roster pointer yet, nobody has deliberated a
/// year that has just opened, and not one student in the whole base has an `AgreementType`. The file
/// was faithful and it still read as broken, because <b>a column empty on every row looks exactly
/// like a column the export forgot to fill</b>. That was reported within minutes of the first
/// download.</para>
///
/// <para>It is the same failure the rest of this system already guards against by name —
/// <c>RepartitionSummary.DeclaredSlotCount</c> separating « no periods » from « periods nobody is
/// in », <c>OutsideYearCount</c> saying what a year filter removed. ⚠ The rule is the one those two
/// encode: <b>an absence has to announce itself</b>, or it stands in for a defect.</para>
/// </summary>
public static class ExportNotes
{
    /// <summary>
    /// The headers of the columns that carry no value in <em>any</em> row.
    ///
    /// <para>Computed from the rows actually exported rather than from a list somebody maintains, so
    /// a column added later is covered without anyone remembering to add it here.</para>
    /// </summary>
    public static IReadOnlyList<string> EmptyColumns(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<ExportCell>> rows)
    {
        if (rows.Count == 0)
            return [];

        var empty = new List<string>();

        for (int c = 0; c < columns.Count; c++)
        {
            bool anyValue = false;
            foreach (var row in rows)
            {
                if (c < row.Count && row[c].HasValue)
                {
                    anyValue = true;
                    break;
                }
            }

            if (!anyValue)
                empty.Add(columns[c].Header);
        }

        return empty;
    }

    /// <summary>
    /// How many of <paramref name="rows"/> carry a value in the column headed
    /// <paramref name="header"/>, and how many rows there are.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The half <see cref="EmptyColumns"/> cannot see.</b> That one asks « is this column
    /// blank on <i>every</i> row », which was the right question while no promotion had been cut at
    /// all. It is the wrong question now: on 13/09/2026 the 3ᵉ MED was cut and planned, so the roll of
    /// 2026-2027 exports <b>933 lines carrying a group out of 6 839</b> — the column is no longer
    /// empty, so no note fires, and the reader gets a column blank on 86% of lines with nothing
    /// explaining it. Reported in exactly those terms: « it is partially working ».</para>
    ///
    /// <para>⚠ <b>Deliberately not a blanket « sparse column » note.</b> Half the columns of a roll are
    /// legitimately partial — a CIN, a CNE, a date of birth — and a note that fires on all of them is
    /// noise, which is dismissed, which puts the real one out of sight. It is asked for the one column
    /// whose blank has two opposite remedies.</para>
    /// </remarks>
    public static (int Filled, int Total) ColumnFill(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<ExportCell>> rows,
        string header)
    {
        int index = -1;
        for (int c = 0; c < columns.Count; c++)
            if (columns[c].Header == header)
            {
                index = c;
                break;
            }

        if (index < 0)
            return (0, rows.Count);

        int filled = rows.Count(row => index < row.Count && row[index].HasValue);
        return (filled, rows.Count);
    }

    /// <summary>
    /// That list as the sentence the document prints, or null when every column carries something.
    /// </summary>
    /// <remarks>
    /// Deliberately says « aucune valeur … dans cet export » rather than « données manquantes ». The
    /// blanks are usually not missing data at all: an empty `Convention` means nobody is under one,
    /// an empty `Source de la décision` means the year has not been deliberated. The note's job is to
    /// tell the reader the export looked and found nothing — not to accuse the base.
    /// </remarks>
    public static string? EmptyColumnsNote(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<ExportCell>> rows)
    {
        var empty = EmptyColumns(columns, rows);

        return empty.Count == 0
            ? null
            : $"Aucune valeur dans cet export pour : {string.Join(", ", empty)}. "
              + "Ces colonnes sont vides parce que la donnée n'existe pas encore, pas parce qu'elles "
              + "n'ont pas été lues.";
    }

    /// <summary>
    /// Why the roster columns are empty — and the two causes call for opposite acts, which is exactly
    /// why the count is stated rather than left to be guessed.
    /// </summary>
    /// <remarks>
    /// ⚠ Same shape as <c>RepartitionSummary.DeclaredSlotCount</c>: no rosters at all means « cut the
    /// promotion into groups », rosters holding nobody means « the cut exists, now assign the
    /// students ». A single blank column collapses the two into one unreadable state, and the reader
    /// cannot tell either from « the export is broken ».
    /// </remarks>
    /// <summary>
    /// Why « Origine du chef » reads « Note (import) » on every row, and why a service whose chef is
    /// linked in Personnel names nobody — or null when the document resolves the full authority
    /// order and the column is telling the reader something row by row.
    /// </summary>
    /// <remarks>
    /// ⚠ A uniform column is the mirror of an empty one: it looks like a value the export hard-coded
    /// rather than a policy somebody chose, and the choice is invisible from the file. It is also the
    /// half of <see cref="ServiceChefSourcePolicy.SourceNoteOnly"/> that <em>costs</em> something — a
    /// service named only by an affectation comes out blank — and a blank nobody explained is the
    /// defect <see cref="ExportNotes"/> exists for. Silent under
    /// <see cref="ServiceChefSourcePolicy.Authority"/>: a note that fires whatever the policy says is
    /// noise, and noise is dismissed.
    /// </remarks>
    public static string? ChefSourceNote(ServiceChefSourcePolicy policy) =>
        policy is ServiceChefSourcePolicy.Authority
            ? null
            : "Les chefs de service sont repris de la fiche du service (note d'import) uniquement : "
              + "aucune affectation de chef n'est lue pour l'instant, les seules enregistrées étant "
              + "des liens de test. Un service dont le chef n'est nommé que par une affectation "
              + "apparaît donc sans nom.";

    /// <param name="filled">Lignes de l'export portant un groupe.</param>
    /// <param name="total">Lignes de l'export.</param>
    /// <remarks>
    /// ⚠ <b>Trois états, trois phrases, et un silence.</b> « aucun groupe n'existe », « les groupes
    /// existent mais personne n'y est », « certaines promotions sont réparties et d'autres non » et
    /// « tout le monde en a » appellent des gestes différents — et le troisième est désormais
    /// l'ordinaire, puisque la faculté répartit une promotion à la fois. Une seule phrase pour les
    /// quatre laisserait le lecteur conclure que l'export est cassé, ce qui est précisément ce qui a
    /// été rapporté.
    /// </remarks>
    public static string? RosterNote(int rostersInScope, int filled, int total)
    {
        // Rien à signaler : la colonne est pleine. Une note qui se déclenche quoi qu'il arrive est du
        // bruit, et le bruit est ignoré.
        if (total == 0 || filled == total)
            return null;

        if (filled > 0)
            return $"{filled} ligne(s) sur {total} portent un groupe. Les autres appartiennent à des "
                 + "promotions qui n'ont pas encore été réparties en groupes : la colonne est vide "
                 + "pour elles parce que la donnée n'existe pas, pas parce qu'elle n'a pas été lue.";

        return rostersInScope == 0
            ? "Aucune inscription n'est rattachée à un groupe, et aucun groupe n'existe encore pour "
              + "cette sélection : la promotion n'a pas été découpée."
            : $"Aucune inscription n'est rattachée à un groupe, alors que {rostersInScope} groupe(s) "
              + "existent pour cette sélection : le découpage est fait, la répartition des étudiants "
              + "ne l'est pas encore.";
    }
}
