namespace PGSH.Application.Students.GetHistory;

public sealed record StudentHistoryResponse(
    Guid Id,
    string HistoryType, // The string name of the Enum; serializes as "historyType" (frontend contract)
    DateTime CreatedAt,
    object? Metadata);
