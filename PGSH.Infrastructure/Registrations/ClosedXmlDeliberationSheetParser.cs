using System.Text;
using System.Globalization;
using ClosedXML.Excel;
using PGSH.Application.Students.Registrations.Deliberation;

namespace PGSH.Infrastructure.Registrations;

/// <summary>
/// The .xlsx side of the déliberation import. Deliberately dumb: it locates the columns by header and
/// hands every cell on to the planner as it found it. Anything it cannot make sense of becomes a null
/// on that row rather than an exception, so one bad cell is reported against its own line in the
/// preview instead of failing the whole upload with nothing to show for it.
///
/// <para>Both canvas modes produce the <em>same</em> decision sheet — same headers, same first three
/// columns — so <see cref="Parse"/> never learns which one it is reading. What differs is only what is
/// already written in it: every student under <see cref="DeliberationTemplateMode.Full"/>, none at all
/// under <see cref="DeliberationTemplateMode.Exceptions"/>, where the roll moves to a reference tab.</para>
/// </summary>
internal sealed class ClosedXmlDeliberationSheetParser : IDeliberationSheetParser
{
    private const string CneHeader = "cne";
    private const string AppogeeHeader = "apogee";
    private const string DecisionHeader = "decision";
    private const string MotifHeader = "motif";

    private static readonly string[] TemplateHeaders = ["CNE", "Apogée", "Décision", "Motif"];

    /// <summary>Offered as a dropdown so the common case never produces an unrecognised word. The
    /// planner accepts far more spellings than these — a hand-built file is still readable.</summary>
    private static readonly string[] Decisions =
        ["Admis", "Redoublant", "Exclu", "Diplômé", "Abandon"];

    /// <summary>How far down the empty exceptions sheet the dropdown and the borders reach. Past it the
    /// file still parses — the validation is a convenience, not the contract.</summary>
    private const int BlankExceptionRows = 300;

    public IReadOnlyList<DeliberationRow> Parse(Stream sheet)
    {
        using var workbook = new XLWorkbook(sheet);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
            return [];

        var rows = used.RowsUsed().ToList();
        if (rows.Count == 0)
            return [];

        var columns = MapHeaders(rows[0]);
        var parsed = new List<DeliberationRow>();

        foreach (var row in rows.Skip(1))
        {
            string? cne = Text(row, columns, CneHeader);
            string? appogee = Text(row, columns, AppogeeHeader);
            string? decision = Text(row, columns, DecisionHeader);
            string? motif = Text(row, columns, MotifHeader);

            // A line the user left completely blank is not a mistake — it is the end of their data.
            if (cne is null && appogee is null && decision is null && motif is null)
                continue;

            parsed.Add(new DeliberationRow(row.RowNumber(), cne, appogee, decision, motif));
        }

        return parsed;
    }

    public byte[] BuildTemplate(DeliberationTemplate template)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Déliberation");

        for (int i = 0; i < TemplateHeaders.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = TemplateHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF1, 0xF5, 0xF9);
        }

        int lastRow = template.Mode == DeliberationTemplateMode.Exceptions
            ? BuildExceptionsSheet(workbook, sheet, template)
            : BuildFullSheet(sheet, template);

        // A closed list on the column that decides a year: a typo here is a row the import refuses,
        // and the jury finds out at upload rather than while filling the sheet.
        sheet.Range(2, 3, lastRow, 3)
            .CreateDataValidation()
            .List($"\"{string.Join(",", Decisions)}\"", true);

        AddInstructions(workbook, template);

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    /// <summary>One decision row per student, pre-filled with the verdict already on record.</summary>
    private static int BuildFullSheet(IXLWorksheet sheet, DeliberationTemplate template)
    {
        int rowNumber = 2;
        foreach (var student in template.Students)
        {
            sheet.Cell(rowNumber, 1).Value = student.Cne;
            sheet.Cell(rowNumber, 2).Value = student.Appogee;

            // A verdict already recorded comes back pre-filled, so a correction pass means editing the
            // two lines that were wrong rather than retyping the promotion.
            if (student.CurrentDecision is { } current)
                sheet.Cell(rowNumber, 3).Value = FrenchDecision(current);

            // Name, level and group are context for whoever fills the sheet, not part of the contract —
            // the import matches on CNE / Apogée and ignores anything past the last named column.
            sheet.Cell(rowNumber, 6).Value = student.FullName;
            sheet.Cell(rowNumber, 7).Value = student.LevelLabel;
            sheet.Cell(rowNumber, 8).Value = student.GroupLabel;
            rowNumber++;
        }

        int lastRow = Math.Max(2, rowNumber - 1);

        sheet.Cell(1, 6).Value = "Étudiant (indicatif)";
        sheet.Cell(1, 7).Value = "Niveau (indicatif)";
        sheet.Cell(1, 8).Value = "Groupe (indicatif)";
        sheet.Range(1, 6, 1, 8).Style.Font.Italic = true;
        sheet.Range(1, 6, lastRow, 8).Style.Font.FontColor = XLColor.Gray;

        return lastRow;
    }

    /// <summary>
    /// An empty decision sheet, plus the roll on its own tab. The jury types only the students the
    /// year went badly for; the import reads everyone else as admis.
    /// </summary>
    private static int BuildExceptionsSheet(
        XLWorkbook workbook, IXLWorksheet sheet, DeliberationTemplate template)
    {
        sheet.Cell(1, 5).Value =
            "⚠ Ne saisissez ici que les exceptions. Tout étudiant absent de cette feuille sera "
            + "enregistré « Admis » — sauf en dernière année, où rien n'est enregistré sans décision "
            + "explicite : c'est à vous d'y nommer les diplômés.";
        sheet.Cell(1, 5).Style.Font.Bold = true;
        sheet.Cell(1, 5).Style.Font.FontColor = XLColor.FromArgb(0xB4, 0x53, 0x09);

        int lastRow = 1 + BlankExceptionRows;
        sheet.Range(2, 1, lastRow, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
        sheet.Columns(1, 4).Width = 18;

        BuildReferenceSheet(workbook, template);
        return lastRow;
    }

    /// <summary>
    /// Every student of the scope, so an identifier is copied rather than retyped. Read-only context:
    /// nothing on this tab is parsed.
    /// </summary>
    private static void BuildReferenceSheet(XLWorkbook workbook, DeliberationTemplate template)
    {
        var sheet = workbook.AddWorksheet("Étudiants (référence)");
        string[] headers = ["CNE", "Apogée", "Étudiant", "Niveau", "Groupe", "Décision enregistrée"];

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF1, 0xF5, 0xF9);
        }

        int rowNumber = 2;
        foreach (var student in template.Students)
        {
            sheet.Cell(rowNumber, 1).Value = student.Cne;
            sheet.Cell(rowNumber, 2).Value = student.Appogee;
            sheet.Cell(rowNumber, 3).Value = student.FullName;
            sheet.Cell(rowNumber, 4).Value = student.LevelLabel;
            sheet.Cell(rowNumber, 5).Value = student.GroupLabel;

            // A verdict already recorded is worth seeing here: the default will not overwrite it, so a
            // student showing one is a student the file must name again to change.
            if (student.CurrentDecision is { } current)
                sheet.Cell(rowNumber, 6).Value = FrenchDecision(current);

            rowNumber++;
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
    }

    /// <summary>The stored <c>RegistrationStatus</c> name written the way the canvas asks for it, so a
    /// re-downloaded sheet round-trips through the parser unchanged.</summary>
    private static string FrenchDecision(string status) => status switch
    {
        "Validated" => "Admis",
        "Failed" => "Redoublant",
        "Excluded" => "Exclu",
        "Graduated" => "Diplômé",
        "Withdrawn" => "Abandon",
        _ => "",
    };

    private static void AddInstructions(XLWorkbook workbook, DeliberationTemplate template)
    {
        var sheet = workbook.AddWorksheet("Mode d'emploi");
        bool exceptions = template.Mode == DeliberationTemplateMode.Exceptions;

        var lines = new List<string>
        {
            $"Déliberation — {template.ScopeLabel}, année universitaire {template.AcademicYearLabel}",
            $"{template.Students.Count} étudiant(s) inscrit(s).",
            "",
        };

        lines.AddRange(exceptions
            ? [
                "Ce canevas est une liste d'EXCEPTIONS.",
                "    Saisissez une ligne par étudiant dont l'année n'est pas simplement acquise.",
                "    Tout étudiant absent de la feuille « Déliberation » sera enregistré Admis.",
                "",
                "    ⚠ SAUF EN DERNIÈRE ANNÉE. Pour un étudiant dont l'année peut être la dernière de",
                "    son CNPN, rien n'est enregistré sans décision explicite : rester en dernière année",
                "    (thèse non soutenue) est aussi courant que la terminer, et PGSH n'a aucune trace",
                "    d'une soutenance. NOMMEZ-Y VOS DIPLÔMÉS — la liste des soutenances est justement",
                "    le document dont vous disposez. Les autres restent en cours, sans décision.",
                "",
                "    L'onglet « Étudiants (référence) » liste les inscrits : copiez-y les identifiants.",
                "    Un étudiant portant déjà une décision enregistrée n'est JAMAIS modifié par défaut ;",
                "    pour changer la sienne, inscrivez-le explicitement dans la feuille.",
                "",
              ]
            : [
                "Ce canevas liste tous les inscrits : une décision par étudiant.",
                "",
              ]);

        lines.AddRange([
            "Colonne « Décision » :",
            "    Admis        l'année est acquise, l'étudiant passe au niveau suivant.",
            "    Redoublant   l'année n'est pas acquise, l'étudiant refait le même niveau.",
            "    Exclu        fin du cursus prononcée par la faculté.",
            "    Diplômé      dernière année du CNPN acquise.",
            "    Abandon      l'étudiant s'est retiré de lui-même.",
            "",
            "Colonne « Motif » — facultative, et retenue uniquement pour Redoublant, Exclu et Abandon.",
            "",
            "Ne modifiez pas les en-têtes.",
            "Une ligne laissée entièrement vide est ignorée.",
            "L'import est appliqué en totalité ou pas du tout : une seule ligne en erreur l'annule.",
            "Une décision déjà enregistrée est remplacée — c'est le moyen prévu de corriger un PV.",
            "",
            "La réinscription de l'année suivante est une étape distincte, à lancer en septembre : "
            + "elle lit ces décisions et propose les inscriptions correspondantes.",
        ]);

        for (int i = 0; i < lines.Count; i++)
            sheet.Cell(i + 1, 1).Value = lines[i];

        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }

    /// <summary>Header text → column number, matched loosely so accents and casing do not matter.</summary>
    private static Dictionary<string, int> MapHeaders(IXLRangeRow header)
    {
        var columns = new Dictionary<string, int>();
        foreach (var cell in header.Cells())
        {
            string? key = Fold(cell.GetString());
            if (key is not null && !columns.ContainsKey(key))
                columns[key] = cell.Address.ColumnNumber;
        }
        return columns;
    }

    private static string? Text(IXLRangeRow row, IReadOnlyDictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out int column))
            return null;

        string value = row.Worksheet.Cell(row.RowNumber(), column).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Lower-cases, trims and strips accents so "Décision", "DECISION" and "decision" are one header.</summary>
    private static string? Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.Normalize(NormalizationForm.FormC);
    }
}
