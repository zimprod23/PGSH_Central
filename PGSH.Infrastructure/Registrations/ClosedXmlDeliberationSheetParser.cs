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

        int rowNumber = 2;
        foreach (var student in template.Students)
        {
            sheet.Cell(rowNumber, 1).Value = student.Cne;
            sheet.Cell(rowNumber, 2).Value = student.Appogee;

            // A verdict already recorded comes back pre-filled, so a correction pass means editing the
            // two lines that were wrong rather than retyping the promotion.
            if (student.CurrentDecision is { } current)
                sheet.Cell(rowNumber, 3).Value = FrenchDecision(current);

            // Name and group are context for whoever fills the sheet, not part of the contract — the
            // import matches on CNE / Apogée and ignores anything past the last named column.
            sheet.Cell(rowNumber, 6).Value = student.FullName;
            sheet.Cell(rowNumber, 7).Value = student.GroupLabel;
            rowNumber++;
        }

        int lastRow = Math.Max(2, rowNumber - 1);

        sheet.Cell(1, 6).Value = "Étudiant (indicatif)";
        sheet.Cell(1, 7).Value = "Groupe (indicatif)";
        sheet.Range(1, 6, 1, 7).Style.Font.Italic = true;
        sheet.Range(1, 6, lastRow, 7).Style.Font.FontColor = XLColor.Gray;

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
        var lines = new List<string>
        {
            $"Déliberation — {template.LevelLabel}, année universitaire {template.AcademicYearLabel}",
            $"{template.Students.Count} étudiant(s) inscrit(s).",
            "",
            "Colonne « Décision » — une valeur par étudiant :",
            "    Admis        l'année est acquise, l'étudiant passe au niveau suivant.",
            "    Redoublant   l'année n'est pas acquise, l'étudiant refait le même niveau.",
            "    Exclu        fin du cursus prononcée par la faculté.",
            "    Diplômé      dernière année du CNPN acquise.",
            "    Abandon      l'étudiant s'est retiré de lui-même.",
            "",
            "Colonne « Motif » — facultative, et retenue uniquement pour Redoublant, Exclu et Abandon.",
            "",
            "Ne modifiez ni les en-têtes ni les colonnes CNE / Apogée déjà remplies.",
            "Une ligne laissée entièrement vide est ignorée.",
            "L'import est appliqué en totalité ou pas du tout : une seule ligne en erreur l'annule.",
            "Une décision déjà enregistrée est remplacée — c'est le moyen prévu de corriger un PV.",
            "",
            "La réinscription de l'année suivante est une étape distincte, à lancer en septembre : "
            + "elle lit ces décisions et propose les inscriptions correspondantes.",
        };

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
