using PGSH.Application.Exports;
using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// Turns a réinscription plan into the three-sheet document scolarité works from.
/// </summary>
/// <remarks>
/// <para><b>Three sheets, because three different questions are asked of one upload.</b>
/// « Synthèse » is what the operator reads before confirming — the counts, in one column of labelled
/// numbers. « Lignes » is one row per line of the faculty's file, which is the sheet somebody walks
/// down looking for the students the roll could not place. « Absents » is one row per closing-year
/// registration the file never mentions, which is a different population entirely: those students are
/// not on the file at all, so no line of « Lignes » could ever carry them.</para>
///
/// <para>⚠ <b>Every row, always.</b> The screen's report is capped and ordered attention-first
/// precisely so a browser survives it; a document has the opposite obligation. This is written from
/// <c>ReinscriptionSheetPlan.AllRows</c> and <c>AllAbsentees</c>, never from
/// <c>Report.Rows</c>/<c>Report.Absentees</c> — reading those would silently stop at 1 000 lines
/// while looking exactly like a complete file, which is the failure the export exists to end.</para>
///
/// <para><b>Sheet order follows the act, not the counts.</b> Synthèse first because it is what
/// justifies pressing Confirmer; Lignes in <em>sheet order</em>, not attention-first, because the
/// reader has the faculty's own file open beside it and line 4 312 has to be line 4 312. The screen
/// sorts by attention for the opposite reason — it can only show a thousand, so the ones that matter
/// must be in them.</para>
///
/// <para>⚠ <b>« Signalé » is a column, not a status.</b> A held row is a row that <em>was</em>
/// registered, and burying that in the status column would make the export disagree with what the
/// apply did. The status says what happened to the line; the flag columns say what was raised on the
/// registration it produced.</para>
/// </remarks>
internal static class ReinscriptionSheetWorkbook
{
    public static ExportWorkbook Build(ReinscriptionSheetPlan plan)
    {
        var report = plan.Report;

        string fileName = ExportFileName.Build(
            "reinscription", report.FromYearLabel, report.ToYearLabel);

        return new ExportWorkbook(fileName,
        [
            Summary(plan),
            Lines(plan),
            Absentees(plan),
        ]);
    }

    // -----------------------------------------------------------------------------------------
    // Synthèse
    // -----------------------------------------------------------------------------------------

    private static ExportSheet Summary(ReinscriptionSheetPlan plan)
    {
        var r = plan.Report;

        var rows = new List<IReadOnlyList<ExportCell>>();

        void Line(string section, string label, int value, string meaning) =>
            rows.Add([
                ExportCell.Text(section),
                ExportCell.Text(label),
                ExportCell.Count(value),
                ExportCell.Paragraph(meaning),
            ]);

        Line("Fichier", "Lignes lues", r.TotalRows,
            "Nombre de lignes de données dans le fichier de la faculté.");
        Line("Fichier", "Lignes en erreur", r.ErrorCount,
            r.ErrorCount == 0
                ? "Aucune. Le fichier peut être appliqué."
                : "Une seule suffit à refuser tout le fichier : la ligne est fausse, et l'écriture "
                  + "qu'elle produirait est une décision sur l'année de quelqu'un.");

        Line("Inscriptions", "Créées", r.WillRegister,
            "Inscriptions de l'année d'arrivée créées à partir du fichier, signalements compris.");
        Line("Inscriptions", "Dont signalées", r.WillRegisterHeld,
            "Créées puis gelées : dernière année de leur CNPN avec des stages antérieurs non validés. "
            + "Elles ne participent ni au découpage en groupes ni aux affectations tant que le "
            + "signalement n'est pas levé.");
        Line("Inscriptions", "Sans inscription source", r.WithoutSourceRegistration,
            "L'étudiant ne portait pas d'inscription sur l'année qui se ferme : rien à prononcer, "
            + "mais l'inscription d'arrivée est créée.");
        Line("Inscriptions", "Décisions enregistrées", r.WillRecordOutcome,
            "Verdicts portés sur l'année qui se ferme, déduits du mouvement de niveau que le fichier "
            + "énonce. Toujours moins que les inscriptions créées : un redoublement de dernière année "
            + "n'est pas un échec et ne porte aucun verdict.");

        Line("Lignes non traitées", "Déjà réinscrits", r.AlreadyRegistered,
            "L'inscription existe déjà pour l'année d'arrivée. Le fichier est rejouable.");
        Line("Inscriptions", "Étudiants créés", r.CreatedStudents,
            "Le fichier les nomme et PGSH ne les connaissait pas : ils sont créés à partir du numéro "
            + "Apogée et du nom, puis signalés « dossier à compléter ». Ce signalement ne gèle pas : "
            + "ils entrent dans les groupes et la planification comme les autres, en attendant qu'on "
            + "complète leur fiche.");
        Line("Inscriptions", "Adresses e-mail générées", r.GeneratedEmails,
            "Users.Email est obligatoire et unique, et une adresse sert d'identifiant de connexion : "
            + "elles sont attribuées contre les adresses déjà en base, jamais seulement contre le "
            + "fichier. Chaque adresse figure sur la ligne de l'étudiant.");
        Line("Lignes non traitées", "Hors périmètre", r.OutsideScope,
            "Filières que PGSH ne gère pas (les masters). Ignorées volontairement.");

        Line("Absents du fichier", "Total", r.NotCovered,
            "Inscriptions de l'année qui se ferme qu'aucune ligne du fichier ne mentionne. Le fichier "
            + "est la liste de ceux qui reviennent : une absence dit qu'ils ne reviennent pas, sans "
            + "dire pourquoi.");
        Line("Absents du fichier", "Enregistrés « Diplômé »", r.WillGraduate,
            "Absents en dernière année de leur propre CNPN : soutenance déduite. Enregistré à titre "
            + "déduit, jamais déclaré — le dépôt ultérieur d'une liste de soutenances le corrigera "
            + "de lui-même.");
        Line("Absents du fichier", "À trancher à la main", r.AbsentNeedingAttention,
            "Absents hors dernière année, ou sans CNPN enregistré : abandon, exclusion ou "
            + "réinscription tardive — rien dans le fichier ne les distingue. Aucune écriture.");
        Line("Absents du fichier", "Portant déjà une décision", r.AbsentAlreadyDecided,
            "Leur année est déjà tranchée et n'est pas retouchée. L'absence reste à expliquer.");
        Line("Absents du fichier", "Signalés", r.AbsenteesHeld,
            "Tous les absents sont gelés, les diplômés compris : la soutenance est une déduction de "
            + "PGSH et non une déclaration de la faculté. Le signalement empêche d'agir dessus avant "
            + "qu'un humain l'ait confirmée.");

        var columns = new[]
        {
            new ExportColumn("Rubrique", 22),
            new ExportColumn("Indicateur", 30),
            new ExportColumn("Nombre", 12),
            new ExportColumn("Ce que cela veut dire", 90),
        };

        return new ExportSheet(
            "Synthèse",
            $"Réinscription {r.FromYearLabel} → {r.ToYearLabel} — "
            + $"{ExportLabels.Count(r.TotalRows)} ligne(s) lue(s), "
            + $"{ExportLabels.Count(r.NotCovered)} inscription(s) absente(s) du fichier.",
            columns,
            rows,
            Notes(r));
    }

    private static IReadOnlyList<string> Notes(ReinscriptionSheetReport r)
    {
        var notes = new List<string>();

        if (!r.CanApply)
            notes.Add(
                $"⚠ Ce fichier ne peut pas être appliqué en l'état : {ExportLabels.Count(r.ErrorCount)} "
                + "ligne(s) en erreur. Elles sont en tête de la feuille « Lignes ».");

        if (r.WillGraduate > 0)
            notes.Add(
                $"L'application demande de confirmer le nombre de diplômés déduits ({ExportLabels.Count(r.WillGraduate)}). "
                + "C'est la seule écriture qui porte sur des étudiants que le fichier ne nomme pas, et "
                + "elle met fin à un cursus — ce qu'aucun bouton ne défait.");

        if (r.AbsenteesHeld > 0 || r.WillRegisterHeld > 0)
            notes.Add(
                $"{ExportLabels.Count(r.AbsenteesHeld + r.WillRegisterHeld)} inscription(s) sont gelées "
                + "à l'issue de cet import : elles n'entrent dans aucun groupe et ne reçoivent aucune "
                + "affectation de stage tant que le signalement n'est pas levé, un étudiant à la fois, "
                + "depuis la page « Signalements ».");

        if (r.RowsTruncated || r.AbsenteesTruncated)
            notes.Add(
                "L'écran limite les listes qu'il affiche ; ce document ne les limite pas. "
                + "Les feuilles « Lignes » et « Absents » sont complètes.");

        return notes;
    }

    // -----------------------------------------------------------------------------------------
    // Lignes
    // -----------------------------------------------------------------------------------------

    private static ExportSheet Lines(ReinscriptionSheetPlan plan)
    {
        var columns = new[]
        {
            new ExportColumn("Ligne", 8),
            new ExportColumn("Code (Apogée)", 16),
            new ExportColumn("Étudiant", 30),
            new ExportColumn("Étape source", 24),
            new ExportColumn("Étape cible", 24),
            new ExportColumn("Traitement", 26),
            new ExportColumn("À traiter", 11),
            new ExportColumn("Décision portée", 18),
            new ExportColumn("Signalement", 30),
            new ExportColumn("Détail", 90),
        };

        var rows = plan.AllRows
            .OrderBy(r => r.SheetRow)
            .Select(r => (IReadOnlyList<ExportCell>)
            [
                ExportCell.Count(r.SheetRow),
                // ⚠ Text, never a number: an Apogée that looks numeric must not lose a leading zero,
                // and it is the column the file is joined back on.
                ExportCell.Text(r.Code),
                ExportCell.Text(r.StudentFullName),
                ExportCell.Text(r.FromLevelLabel),
                ExportCell.Text(r.ToLevelLabel),
                ExportCell.Text(StatusLabel(r.Status)),
                ExportCell.YesNo(r.Status.NeedsAttention()),
                ExportCell.Text(r.Outcome is { } outcome ? ExportLabels.RegistrationStatus(outcome) : null),
                ExportCell.Text(r.Status == ReinscriptionSheetRowStatus.WillRegisterHeld
                    ? RegistrationHoldReason.OutstandingPriorStages.Label()
                    : null),
                ExportCell.Paragraph(r.Message),
            ])
            .ToList();

        return new ExportSheet(
            "Lignes",
            $"Une ligne par ligne du fichier, dans l'ordre du fichier — "
            + $"{ExportLabels.Count(rows.Count)} au total.",
            columns,
            rows,
            ExportNotes.EmptyColumnsNote(columns, rows) is { } note ? [note] : null);
    }

    // -----------------------------------------------------------------------------------------
    // Absents
    // -----------------------------------------------------------------------------------------

    private static ExportSheet Absentees(ReinscriptionSheetPlan plan)
    {
        var columns = new[]
        {
            new ExportColumn("Code (Apogée)", 16),
            new ExportColumn("Étudiant", 30),
            new ExportColumn("Niveau", 26),
            new ExportColumn("Lecture de l'absence", 26),
            new ExportColumn("Décision enregistrée", 20),
            new ExportColumn("Gelé", 8),
            new ExportColumn("Détail", 90),
            new ExportColumn("Ce qu'il reste à faire", 70),
        };

        var rows = plan.AllAbsentees
            .OrderBy(a => a.LevelLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.StudentFullName, StringComparer.OrdinalIgnoreCase)
            .Select(a => (IReadOnlyList<ExportCell>)
            [
                ExportCell.Text(a.Appogee),
                ExportCell.Text(a.StudentFullName),
                ExportCell.Text(a.LevelLabel),
                ExportCell.Text(AbsenceLabel(a.Outcome)),
                ExportCell.Text(a.Outcome == ReinscriptionSheetAbsenceOutcome.Graduating
                    ? "Diplômé (déduit)"
                    : null),
                // Every absentee is held, so this column is deliberately constant — and it is the one
                // column whose constancy is the message rather than a gap. ExportNotes would flag a
                // column that were constantly *empty*; this one is constantly full, on purpose.
                ExportCell.YesNo(true),
                ExportCell.Paragraph(a.Message),
                ExportCell.Paragraph(RegistrationHoldReason.AbsentFromReinscriptionRoll.Remedy()),
            ])
            .ToList();

        return new ExportSheet(
            "Absents",
            $"Inscriptions de {plan.Report.FromYearLabel} qu'aucune ligne du fichier ne mentionne — "
            + $"{ExportLabels.Count(rows.Count)} au total. Toutes sont gelées.",
            columns,
            rows,
            ExportNotes.EmptyColumnsNote(columns, rows) is { } note ? [note] : null);
    }

    // -----------------------------------------------------------------------------------------
    // Wording
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The French wording of this report's own two enums.
    /// </summary>
    /// <remarks>
    /// Kept here rather than in <c>ExportLabels</c>, which covers the domain enums several exports
    /// share. These two have exactly one consumer, and the rule that class encodes is about two
    /// documents disagreeing — not about centralising for its own sake. The frontend translates the
    /// same enum names separately, which is the split already stated there.
    /// </remarks>
    private static string StatusLabel(ReinscriptionSheetRowStatus status) => status switch
    {
        ReinscriptionSheetRowStatus.WillRegister => "Réinscrit",
        ReinscriptionSheetRowStatus.WillRegisterWithoutSource => "Réinscrit — sans année source",
        ReinscriptionSheetRowStatus.WillRegisterHeld => "Réinscrit — signalé",
        ReinscriptionSheetRowStatus.AlreadyRegistered => "Déjà réinscrit",
        ReinscriptionSheetRowStatus.OutsideScope => "Hors périmètre",
        ReinscriptionSheetRowStatus.WillCreateStudent => "Créé — dossier à compléter",
        ReinscriptionSheetRowStatus.NoIdentifier => "Erreur — code absent",
        ReinscriptionSheetRowStatus.DuplicateRow => "Erreur — code en double",
        ReinscriptionSheetRowStatus.UnknownLevelCode => "Erreur — code étape inconnu",
        ReinscriptionSheetRowStatus.LevelMismatch => "Erreur — étape source contredite",
        ReinscriptionSheetRowStatus.NotAPromotion => "Erreur — niveau sans cursus",
        ReinscriptionSheetRowStatus.LevelRegression => "Erreur — étape en recul",
        ReinscriptionSheetRowStatus.LevelMissing => "Erreur — niveau absent du catalogue",
        _ => status.ToString(),
    };

    private static string AbsenceLabel(ReinscriptionSheetAbsenceOutcome outcome) => outcome switch
    {
        ReinscriptionSheetAbsenceOutcome.Graduating => "Fin de cursus déduite",
        ReinscriptionSheetAbsenceOutcome.AlreadyDecided => "Année déjà tranchée",
        ReinscriptionSheetAbsenceOutcome.NotAPromotion => "Niveau sans cursus",
        ReinscriptionSheetAbsenceOutcome.NoTextOnRecord => "Aucun CNPN enregistré",
        ReinscriptionSheetAbsenceOutcome.BelowFinalYear => "Hors dernière année",
        _ => outcome.ToString(),
    };
}
