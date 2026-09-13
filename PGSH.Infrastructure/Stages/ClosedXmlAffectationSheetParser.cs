using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using PGSH.Application.Stages.InternshipAssignments.Sheet;

namespace PGSH.Infrastructure.Stages;

/// <summary>
/// The .xlsx side of the canevas des affectations. Deliberately dumb: it locates the columns by header
/// and hands every cell on as it found it. Anything it cannot make sense of becomes a null on that row
/// rather than an exception, so one bad cell is reported against its own line in the aperçu instead of
/// failing the whole upload with nothing to show for it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This canvas is PGSH's own</b>, so the header matching can be strict about <i>which</i>
/// columns exist and is deliberately loose about how they are spelled: a file that has been through
/// Excel, LibreOffice and somebody's phone comes back with « DÉBUT », « Debut » or « début ».</para>
///
/// <para>⚠ <b>Dates are read as dates first, as text second.</b> Excel stores a date as a number, and
/// <c>GetString()</c> on one of those returns the serial (« 46 274 ») — which parses as nothing and
/// would report « date illisible » on a perfectly good cell. The text fallback exists for the columns
/// somebody has retyped by hand, and it accepts the two spellings a Moroccan keyboard produces:
/// <c>jj/mm/aaaa</c> and ISO.</para>
///
/// <para><c>Apogée</c> arrives as a number, not text — the legacy <c>NO_ORDRE</c> is all digits — so it
/// is read as one where the cell holds one and formatted without a separator or a decimal point, or it
/// comes back as <c>2.4008386E7</c> and matches nobody.</para>
/// </remarks>
internal sealed class ClosedXmlAffectationSheetParser : IAffectationSheetParser
{
    private const string AppogeeHeader = "apogee";
    private const string CneHeader = "cne";
    private const string StageHeader = "stage";
    private const string ServiceHeader = "service";
    private const string HospitalHeader = "hopital";
    private const string StartHeader = "debut";
    private const string EndHeader = "fin";
    private const string ReasonHeaderPrefix = "motif";

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd.MM.yyyy",
    ];

    public IReadOnlyList<AffectationSheetRow> Parse(Stream sheet)
    {
        using var workbook = new XLWorkbook(sheet);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
            return [];

        var rows = used.RowsUsed().ToList();

        // The canvas prints a caption above the header, so the header is found rather than assumed to
        // be the first line — a caption read as a header names no column at all and the whole file
        // then reports « stage inconnu » on every line.
        int headerIndex = rows.FindIndex(row => MapHeaders(row).ContainsKey(StageHeader));
        if (headerIndex < 0)
            return [];

        var headers = MapHeaders(rows[headerIndex]);

        int? appogee = Exact(headers, AppogeeHeader);
        int? cne = Exact(headers, CneHeader);
        int? stage = Exact(headers, StageHeader);
        int? service = Exact(headers, ServiceHeader);
        int? hospital = Exact(headers, HospitalHeader);
        int? start = Exact(headers, StartHeader);
        int? end = Exact(headers, EndHeader);
        int? reason = Prefixed(headers, ReasonHeaderPrefix);

        var parsed = new List<AffectationSheetRow>();

        foreach (var row in rows.Skip(headerIndex + 1))
        {
            var line = new AffectationSheetRow(
                row.RowNumber(),
                Identifier(row, appogee),
                Identifier(row, cne),
                Text(row, stage),
                Text(row, service),
                Text(row, hospital),
                Day(row, start),
                Day(row, end),
                Text(row, reason));

            // A line the user left completely blank is not a mistake — it is the end of their data.
            if (line is { Appogee: null, Cne: null, StageName: null, ServiceName: null,
                          StartDate: null, EndDate: null })
                continue;

            parsed.Add(line);
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

    private static int? Prefixed(IReadOnlyDictionary<string, int> headers, string prefix) =>
        headers.Where(h => h.Key.StartsWith(prefix, StringComparison.Ordinal))
               .Select(h => (int?)h.Value)
               .FirstOrDefault();

    private static string? Identifier(IXLRangeRow row, int? column)
    {
        if (Cell(row, column) is not { } cell) return null;

        if (cell.DataType == XLDataType.Number && cell.TryGetValue(out double number))
            return number.ToString("0.############################", CultureInfo.InvariantCulture);

        string value = cell.GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static DateOnly? Day(IXLRangeRow row, int? column)
    {
        if (Cell(row, column) is not { } cell) return null;

        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue(out DateTime stored))
            return DateOnly.FromDateTime(stored);

        string value = cell.GetString().Trim();
        if (value.Length == 0) return null;

        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    private static string? Text(IXLRangeRow row, int? column)
    {
        if (Cell(row, column) is not { } cell) return null;

        string value = cell.GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static IXLCell? Cell(IXLRangeRow row, int? column) =>
        column is { } index ? row.Worksheet.Cell(row.RowNumber(), index) : null;

    /// <summary>Lower-cases, trims and strips accents, so « Début », "DEBUT" and "debut" are one header.</summary>
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
