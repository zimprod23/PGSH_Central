using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;

namespace PGSH.Infrastructure.Registrations;

/// <summary>
/// The .xlsx side of the réinscription roll. Deliberately dumb: it locates the columns by header and
/// hands every cell on as it found it. Anything it cannot make sense of becomes a null on that row
/// rather than an exception, so one bad cell is reported against its own line in the preview instead
/// of failing the whole upload with nothing to show for it.
///
/// <para>⚠ <b>This canvas is the faculty's, not PGSH's</b> — which is why there is no
/// <c>BuildTemplate</c> here and why the header matching is looser than the déliberation's. The real
/// 2026-2027 file's headers are <c>Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027</c>, and the
/// two that matter carry a year suffix that will be different next September. So the level columns
/// are found by their <c>Etape</c> prefix and taken <b>in sheet order</b> — leftmost is the year
/// closing, rightmost the year opening — rather than by matching a string that changes annually.</para>
///
/// <para><c>Code</c> arrives as a number, not text: <c>24008386</c> in the source file is an Excel
/// numeric cell, and <c>GetString()</c> on one of those can come back as <c>24008386</c> or as
/// <c>2.4008386E7</c> depending on the cell's format. It is read as a number where it is one and
/// formatted without a separator or a decimal point, so it matches <c>Students.Appogee</c>, which
/// holds the legacy <c>NO_ORDRE</c> as plain digits.</para>
/// </summary>
internal sealed class ClosedXmlReinscriptionSheetParser : IReinscriptionSheetParser
{
    private const string CodeHeader = "code";
    private const string LastNameHeader = "nom";
    private const string FirstNameHeader = "prenom";
    private const string LevelHeaderPrefix = "etape";

    public IReadOnlyList<ReinscriptionSheetRow> Parse(Stream sheet)
    {
        using var workbook = new XLWorkbook(sheet);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
            return [];

        var rows = used.RowsUsed().ToList();
        if (rows.Count == 0)
            return [];

        var headers = MapHeaders(rows[0]);

        // ⚠ « NOM » is a prefix of nothing and « PRENOM » contains it, so an exact fold match is used
        // for the three named columns and a prefix match only for « Etape ».
        int? codeColumn = Exact(headers, CodeHeader);
        int? lastNameColumn = Exact(headers, LastNameHeader);
        int? firstNameColumn = Exact(headers, FirstNameHeader);

        var levelColumns = headers
            .Where(h => h.Key.StartsWith(LevelHeaderPrefix, StringComparison.Ordinal))
            .OrderBy(h => h.Value)
            .Select(h => h.Value)
            .ToList();

        int? fromColumn = levelColumns.Count > 0 ? levelColumns[0] : null;
        int? toColumn = levelColumns.Count > 1 ? levelColumns[1] : null;

        var parsed = new List<ReinscriptionSheetRow>();

        foreach (var row in rows.Skip(1))
        {
            string? code = Identifier(row, codeColumn);
            string? lastName = Text(row, lastNameColumn);
            string? firstName = Text(row, firstNameColumn);
            string? from = Text(row, fromColumn);
            string? to = Text(row, toColumn);

            // A line the user left completely blank is not a mistake — it is the end of their data.
            if (code is null && lastName is null && firstName is null && from is null && to is null)
                continue;

            parsed.Add(new ReinscriptionSheetRow(row.RowNumber(), code, lastName, firstName, from, to));
        }

        return parsed;
    }

    /// <summary>Header text → column number, matched loosely so accents and casing do not matter.</summary>
    private static Dictionary<string, int> MapHeaders(IXLRangeRow header)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in header.Cells())
        {
            string? key = Fold(cell.GetString());
            if (key is not null && !columns.ContainsKey(key))
                columns[key] = cell.Address.ColumnNumber;
        }
        return columns;
    }

    private static int? Exact(IReadOnlyDictionary<string, int> headers, string key) =>
        headers.TryGetValue(key, out int column) ? column : null;

    /// <summary>
    /// A cell that holds an identifier. Read as a number where the sheet stores one, so an Apogée
    /// never arrives as <c>2.4008386E7</c> or with a thousands separator.
    /// </summary>
    private static string? Identifier(IXLRangeRow row, int? column)
    {
        if (column is not { } index) return null;

        var cell = row.Worksheet.Cell(row.RowNumber(), index);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double number))
            return number.ToString("0.############################", CultureInfo.InvariantCulture);

        string value = cell.GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? Text(IXLRangeRow row, int? column)
    {
        if (column is not { } index) return null;

        string value = row.Worksheet.Cell(row.RowNumber(), index).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Lower-cases, trims and strips accents, so « Prénom », "PRENOM" and "prenom" are one header.</summary>
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
