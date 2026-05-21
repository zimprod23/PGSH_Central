namespace PGSH.SharedKernel;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}