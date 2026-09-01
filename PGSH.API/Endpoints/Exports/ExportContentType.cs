namespace PGSH.API.Endpoints.Exports;

/// <summary>
/// The MIME type of an .xlsx download, written once. Spelled wrong it is the browser, not the API,
/// that decides to show the bytes instead of saving them — and the failure looks like a broken
/// endpoint.
/// </summary>
public static class ExportContentType
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
