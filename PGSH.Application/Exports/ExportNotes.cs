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
    public static string RosterNote(int rostersInScope) => rostersInScope == 0
        ? "Aucune inscription n'est rattachée à un groupe, et aucun groupe n'existe encore pour cette "
          + "sélection : la promotion n'a pas été découpée."
        : $"Aucune inscription n'est rattachée à un groupe, alors que {rostersInScope} groupe(s) "
          + "existent pour cette sélection : le découpage est fait, la répartition des étudiants ne "
          + "l'est pas encore.";
}
