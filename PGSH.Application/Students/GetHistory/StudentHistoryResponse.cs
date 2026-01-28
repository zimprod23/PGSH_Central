namespace PGSH.Application.Students.GetHistory;

public sealed record StudentHistoryResponse(
    Guid Id,
    string EventType, // The string name of the Enum
    DateTime CreatedAt,
    object? Metadata);
