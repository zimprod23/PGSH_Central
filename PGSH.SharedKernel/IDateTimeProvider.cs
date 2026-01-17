namespace PGSH.SharedKernel;

public interface IDateTimeProvider
{
    public DateTime UtcNow { get; }
}