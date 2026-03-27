namespace PGSH.SharedKernel;

public sealed record BulkResponse<TIdentifier, TResponse>(
    List<BulkItemResult<TIdentifier, TResponse>> Items,
    int TotalProcessed,
    int SuccessCount,
    int FailureCount)
{
    public bool HasFailures => FailureCount > 0;
}

public sealed record BulkItemResult<TIdentifier, TResponse>(
    TIdentifier Identifier,
    TResponse? Data,
    Error? Error)
{
    public bool IsSuccess => Error == Error.None || Error == null;
}
