using ClosedXML.Excel;
using PGSH.Application.Exports;

namespace PGSH.Infrastructure.Exports;

/// <summary>
/// The .xlsx side of every export — the one place that knows what a spreadsheet is, mirroring the
/// three <c>ClosedXml*SheetParser</c>s in the other direction.
///
/// <para>Every decision about how an exported document <em>looks</em> lives here, once: the caption,
/// the header band, the frozen pane, the auto-filter, the date and number formats. Three handlers
/// each styling their own workbook is three documents that look like three faculties.</para>
///
/// <para>⚠ <b>Values are written in their own type.</b> A date pushed through as text cannot be
/// sorted and a mark pushed through as text cannot be averaged — which is the first thing anybody
/// does to a post-validation file, and it fails silently rather than loudly.</para>
/// </summary>
internal sealed class ClosedXmlExportWorkbookWriter : IExportWorkbookWriter
{
    private const string DateFormat = "dd/mm/yyyy";
    private const string DecimalFormat = "0.00";
    private const string IntegerFormat = "0";

    /// <summary>Excel's own limit on a sheet name, and it truncates silently if we do not.</summary>
    private const int MaxSheetNameLength = 31;

    private static readonly char[] ForbiddenInSheetName = ['[', ']', ':', '*', '?', '/', '\\'];

    public byte[] Write(ExportWorkbook workbook)
    {
        using var xl = new XLWorkbook();

        foreach (var sheet in workbook.Sheets)
            WriteSheet(xl, sheet);

        using var stream = new MemoryStream();
        xl.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(XLWorkbook xl, ExportSheet sheet)
    {
        var worksheet = xl.AddWorksheet(SafeName(sheet.Name));

        int headerRow = 1;
        int preamble = 0;

        if (!string.IsNullOrWhiteSpace(sheet.Caption))
        {
            preamble++;
            var caption = worksheet.Cell(preamble, 1);
            caption.Value = sheet.Caption;
            caption.Style.Font.Bold = true;
            caption.Style.Font.FontSize = 12;

            if (sheet.Columns.Count > 1)
                worksheet.Range(preamble, 1, preamble, sheet.Columns.Count).Merge();
        }

        // ⚠ Printed, not omitted when there is nothing to say — and printed *above* the header, where
        // a reader looking at an empty column is already looking. A note in a far-off cell answers
        // nobody: the whole point is that the blank column is explained where it is seen.
        foreach (string note in sheet.Notes ?? [])
        {
            preamble++;
            var cell = worksheet.Cell(preamble, 1);
            cell.Value = note;
            cell.Style.Font.FontSize = 10;
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.FromArgb(0x6B, 0x72, 0x80);
            cell.Style.Alignment.WrapText = false;

            if (sheet.Columns.Count > 1)
                worksheet.Range(preamble, 1, preamble, sheet.Columns.Count).Merge();
        }

        // One blank line between what the sheet says about itself and the table proper.
        if (preamble > 0)
            headerRow = preamble + 2;

        for (int c = 0; c < sheet.Columns.Count; c++)
        {
            var cell = worksheet.Cell(headerRow, c + 1);
            cell.Value = sheet.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEF, 0xF1, 0xF5);
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Column(c + 1).Width = sheet.Columns[c].Width;
        }

        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            for (int c = 0; c < row.Count && c < sheet.Columns.Count; c++)
                WriteCell(worksheet.Cell(headerRow + 1 + r, c + 1), row[c]);
        }

        // ⚠ Both are on the header row, not on row 1: a caption above it means the frozen pane and
        // the filter would otherwise sit on the title and the sheet would scroll its own headings away.
        worksheet.SheetView.FreezeRows(headerRow);

        if (sheet.Rows.Count > 0)
            worksheet.Range(headerRow, 1, headerRow + sheet.Rows.Count, sheet.Columns.Count)
                .SetAutoFilter();
    }

    private static void WriteCell(IXLCell cell, ExportCell value)
    {
        switch (value.Kind)
        {
            case ExportCellKind.Number when value.Number is { } number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = DecimalFormat;
                break;

            case ExportCellKind.Count when value.Number is { } count:
                cell.Value = count;
                cell.Style.NumberFormat.Format = IntegerFormat;
                break;

            case ExportCellKind.Date when value.Date is { } date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                break;

            case ExportCellKind.Paragraph when value.Value is { } paragraph:
                cell.Value = paragraph;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                break;

            case ExportCellKind.Text when value.Value is { } text:
                // Written as text on purpose: a CNE or an Apogée that looks like a number must not
                // lose its leading zeros, and « 3-4 » must not become a date.
                cell.SetValue(text);
                break;

            default:
                // An empty cell is left genuinely empty rather than filled with "" — the difference
                // shows up in every COUNTA and every filter's « (Vides) ».
                break;
        }
    }

    private static string SafeName(string name)
    {
        string cleaned = new(name.Select(c => ForbiddenInSheetName.Contains(c) ? ' ' : c).ToArray());

        return cleaned.Length <= MaxSheetNameLength
            ? cleaned
            : cleaned[..MaxSheetNameLength];
    }
}
