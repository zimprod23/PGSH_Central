namespace PGSH.Application.Exports;

/// <summary>
/// Renders an <see cref="ExportWorkbook"/> to bytes. The one place that knows what a spreadsheet
/// file is — same split as <c>IInscriptionSheetParser</c>, in the other direction.
/// </summary>
public interface IExportWorkbookWriter
{
    byte[] Write(ExportWorkbook workbook);
}

/// <summary>What a handler hands back: a name and the bytes, ready for <c>Results.File</c>.</summary>
public sealed record ExportFile(string FileName, byte[] Content);
