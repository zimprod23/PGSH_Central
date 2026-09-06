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
    Inscription,
    ValidationStage,
    NonValidation,
    Fraud,
    Revalidation,
    GroupTransfer,
    CohortTransfer,
    Delocalization,

    /// <summary>
    /// A délocalisation was walked back and the student returned to the répartition. Its own type
    /// rather than a metadata field on <see cref="Delocalization"/>: the dossier is read as a
    /// sequence of things that happened, and a cancellation that looked like a délocalisation would
    /// leave the timeline saying the student served the stage abroad twice.
    /// </summary>
    DelocalizationCancelled,

    StatusChange,
}