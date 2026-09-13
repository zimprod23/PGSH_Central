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

    /// <summary>
    /// The student's rotation on one stage was written from an uploaded canevas rather than produced
    /// by the planning grid.
    ///
    /// <para>⚠ Its own type, and not <see cref="ValidationStage"/> or a metadata flag, because the
    /// dossier is the only place that can say <b>where these dates come from</b>. A rotation the grid
    /// published and one a spreadsheet declared look identical afterwards — same périodes, same
    /// services — and they are not the same fact: the second was decided by a person, outside the
    /// répartition, and « pourquoi cet étudiant à ces dates ? » has no other answer.</para>
    /// </summary>
    AffectationImported,

    StatusChange,
}