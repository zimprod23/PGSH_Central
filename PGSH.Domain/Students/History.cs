using PGSH.SharedKernel;

namespace PGSH.Domain.Students;

public sealed class History
{
    public Guid Id { get; set; }
    public HistoryType HistoryData { get; set; }
    public DateTime CreatedAt { get; set; }
    public object? Metadata { get; set; } = null;
    public Student Student { get; set; }
    public Guid StudentId { get; set; }
}

public enum HistoryType
{
    ValidationStage,
    Inscription,
    NonValidation,
    Fraud,
    Revalidation
}