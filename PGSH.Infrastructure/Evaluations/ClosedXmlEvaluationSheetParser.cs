using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Domain.Stages;

namespace PGSH.Infrastructure.Evaluations;

/// <summary>
/// The .xlsx / .csv side of the evaluation import. Deliberately dumb: it locates the columns by
/// header and hands every cell on to the planner as it found it. Anything it cannot make sense of
/// becomes a null on that row rather than an exception, so one bad cell is reported against its own
/// line in the preview instead of failing the whole upload with nothing to show for it.
/// </summary>
internal sealed class ClosedXmlEvaluationSheetParser : IEvaluationSheetParser
{
    private const string CneHeader     = "cne";
    private const string AppogeeHeader = "apogee";
    private const string PeriodHeader  = "periode";
    private const string VerdictHeader = "resultat";
    private const string MarkHeader    = "note";
    private const string RemarkHeader  = "remarque";

    private static readonly string[] TemplateHeaders =
        ["CNE", "Apogée", "Période", "Résultat", "Note", "Remarque"];

    public IReadOnlyList<EvaluationImportRow> Parse(Stream sheet)
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
        var parsed = new List<EvaluationImportRow>();

        foreach (var row in rows.Skip(1))
        {
            string? cne     = Text(row, columns, CneHeader);
            string? appogee = Text(row, columns, AppogeeHeader);
            string? verdict = Text(row, columns, VerdictHeader);
            decimal? mark   = Number(row, columns, MarkHeader);
            int? period     = (int?)Number(row, columns, PeriodHeader);
            string? remark  = Text(row, columns, RemarkHeader);

            // A line the user left completely blank is not a mistake — it is the end of their data.
            if (cne is null && appogee is null && verdict is null && mark is null && remark is null)
                continue;

            parsed.Add(new EvaluationImportRow(
                row.RowNumber(), cne, appogee, period, verdict, mark, remark));
        }

        return parsed;
    }

    public byte[] BuildTemplate(EvaluationImportTemplate template)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Notes");

        for (int i = 0; i < TemplateHeaders.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = TemplateHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF1, 0xF5, 0xF9);
        }

        // The identity columns are pre-filled and the rest left blank: the marker fills in exactly
        // one column, the one the chosen mode expects.
        int rowNumber = 2;
        foreach (var student in template.Students)
        {
            sheet.Cell(rowNumber, 1).Value = student.Cne;
            sheet.Cell(rowNumber, 2).Value = student.Appogee;
            if (template.Scope == EvaluationImportScope.SinglePeriod && template.PeriodNumber is { } p)
                sheet.Cell(rowNumber, 3).Value = p;

            // Name and group are context for whoever fills the sheet, not part of the contract —
            // the import matches on CNE / Apogée and ignores anything past the last column.
            sheet.Cell(rowNumber, 7).Value = student.FullName;
            sheet.Cell(rowNumber, 8).Value = student.GroupLabel;
            rowNumber++;
        }

        sheet.Cell(1, 7).Value = "Étudiant (indicatif)";
        sheet.Cell(1, 8).Value = "Groupe (indicatif)";
        sheet.Range(1, 7, 1, 8).Style.Font.Italic = true;
        sheet.Range(1, 7, Math.Max(1, rowNumber - 1), 8).Style.Font.FontColor = XLColor.Gray;

        AddInstructions(workbook, template);

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static void AddInstructions(XLWorkbook workbook, EvaluationImportTemplate template)
    {
        var sheet = workbook.AddWorksheet("Mode d'emploi");
        var lines = new List<string>
        {
            $"Stage : {template.StageName}",
            template.Scope == EvaluationImportScope.SinglePeriod
                ? $"Portée : période P{template.PeriodNumber} uniquement."
                : "Portée : tout le stage — la valeur saisie est appliquée à chacune de ses rotations.",
            template.Mode == EvaluationMode.Numeric
                ? "Mode : note chiffrée. Remplissez la colonne « Note » (0 à 20). Laissez « Résultat » vide."
                : "Mode : validation globale. Remplissez la colonne « Résultat » avec « Validé » ou "
                  + "« Non validé ». Laissez « Note » vide.",
            "",
            "Ne modifiez ni les en-têtes ni les colonnes CNE / Apogée déjà remplies.",
            "Une ligne laissée entièrement vide est ignorée.",
            "L'import est appliqué en totalité ou pas du tout : une seule ligne en erreur l'annule.",
            "Une note déjà enregistrée est remplacée — c'est le moyen prévu de corriger une saisie.",
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

    private static decimal? Number(IXLRangeRow row, IReadOnlyDictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out int column))
            return null;

        var cell = row.Worksheet.Cell(row.RowNumber(), column);
        if (cell.DataType == XLDataType.Number)
            return (decimal)cell.GetDouble();

        string raw = cell.GetString().Trim().Replace(',', '.');
        return raw.Length > 0
               && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    /// <summary>Lower-cases, trims and strips accents so "Apogée", "APOGEE" and "apogee" are one header.</summary>
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
