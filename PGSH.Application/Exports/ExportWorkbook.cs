namespace PGSH.Application.Exports;

/// <summary>
/// A workbook described in terms of what it <em>says</em>, not of how a spreadsheet stores it.
///
/// <para>Two exports already exist and a third (the pre-validation répartition) is expected, so the
/// alternative was three ClosedXML classes each re-deciding the header fill, the frozen row, the
/// date format and the column widths — i.e. three documents that look like three different
/// faculties. The model lives in the application layer beside the handlers that fill it; the single
/// <see cref="IExportWorkbookWriter"/> in Infrastructure is the only code that knows what .xlsx
/// is.</para>
///
/// <para>⚠ <b>Cells are typed, and that is not cosmetic.</b> A date written as text cannot be sorted,
/// and a mark written as text cannot be averaged — which is the first thing anybody does to a
/// post-validation file. <see cref="ExportCell"/> carries the value in its own type and lets the
/// writer decide the display format.</para>
/// </summary>
public sealed record ExportWorkbook(string FileName, IReadOnlyList<ExportSheet> Sheets);

/// <summary>
/// One sheet. <paramref name="Caption"/> is printed above the header and states the scope the file
/// was cut for — ⚠ a file that does not say which promotion and which year it covers is one nobody
/// can audit three months later, and every export here is scoped by an academic year that the
/// caller was allowed to omit.
///
/// <para><paramref name="Notes"/> are printed under it, and they exist for one reason:
/// ⚠ <b>a column blank on every row is indistinguishable from a column the export forgot.</b>
/// Reported by the user on 2026-08-31, against a perfectly faithful file — « le 4MED a des groupes et
/// je ne les vois pas ». The reads were right (0 of 5 932 inscriptions carried a roster pointer); the
/// document simply had no way to say so, which is the same « one state standing in for two » that
/// <c>RepartitionSummary.DeclaredSlotCount</c> and <c>OutsideYearCount</c> exist to prevent.</para>
/// </summary>
public sealed record ExportSheet(
    string Name,
    string? Caption,
    IReadOnlyList<ExportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ExportCell>> Rows,
    IReadOnlyList<string>? Notes = null);

/// <summary>A column header and how wide it wants to be, in characters.</summary>
public sealed record ExportColumn(string Header, double Width = 16);

public enum ExportCellKind
{
    Text,
    /// <summary>Text holding newlines — the writer wraps it and lets the row grow.</summary>
    Paragraph,
    Number,
    /// <summary>A whole number: same as <see cref="Number"/> but without decimals.</summary>
    Count,
    Date,
}

/// <summary>
/// One cell. Built through the named factories rather than the constructor so a null never has to
/// pick an overload — <c>ExportCell.Text(null)</c> and <c>ExportCell.Number(null)</c> are different
/// empty cells and the compiler cannot tell them apart from a bare <c>null</c>.
/// </summary>
public readonly record struct ExportCell(
    ExportCellKind Kind,
    string? Value,
    decimal? Number,
    DateOnly? Date)
{
    public static readonly ExportCell Empty = new(ExportCellKind.Text, null, null, null);

    public static ExportCell Text(string? value) =>
        new(ExportCellKind.Text, string.IsNullOrWhiteSpace(value) ? null : value, null, null);

    public static ExportCell Paragraph(string? value) =>
        new(ExportCellKind.Paragraph, string.IsNullOrWhiteSpace(value) ? null : value, null, null);

    public static ExportCell Numeric(decimal? value) =>
        new(ExportCellKind.Number, null, value, null);

    public static ExportCell Count(int? value) =>
        new(ExportCellKind.Count, null, value, null);

    public static ExportCell Day(DateOnly? value) =>
        new(ExportCellKind.Date, null, null, value);

    public static ExportCell YesNo(bool value) => Text(value ? "Oui" : "Non");

    /// <summary>Does this cell actually carry something? Drives the empty-column notes.</summary>
    public bool HasValue => Kind switch
    {
        ExportCellKind.Number or ExportCellKind.Count => Number is not null,
        ExportCellKind.Date                           => Date is not null,
        _                                             => !string.IsNullOrWhiteSpace(Value),
    };
}
