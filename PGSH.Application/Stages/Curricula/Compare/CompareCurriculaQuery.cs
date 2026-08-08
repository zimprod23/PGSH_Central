using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Curricula.Compare;

/// <summary>
/// What changed in a level's CNPN between two texts. This is the read behind manual revalidation: a
/// student who failed a stage under the old CNPN is judged against it, but can only be re-planned
/// against today's — so the administration needs both side by side before deciding.
/// </summary>
public sealed record CompareCurriculaQuery(int LevelId, int FromCnpnVersionId, int ToCnpnVersionId)
    : IQuery<CurriculumComparisonResponse>;

public sealed record CurriculumComparisonResponse(
    int    LevelId,
    string? LevelLabel,
    int     FromCnpnVersionId,
    string  FromCnpnVersionLabel,
    int     ToCnpnVersionId,
    string  ToCnpnVersionLabel,
    bool   HasChanges,
    IReadOnlyList<CurriculumDiffEntry> Entries);

public sealed record CurriculumDiffEntry(
    int    StageId,
    string StageName,
    CurriculumChange Change,
    int?   FromCoefficient,
    int?   ToCoefficient,
    int?   FromDurationInDays,
    int?   ToDurationInDays);

public enum CurriculumChange
{
    /// <summary>Required by both texts on the same terms.</summary>
    Unchanged,

    /// <summary>Absent from the earlier text, required by the later one.</summary>
    Added,

    /// <summary>Required earlier, dropped since — the case that strands a failed stage.</summary>
    Removed,

    /// <summary>Kept, but with a different coefficient or duration.</summary>
    Reweighted,
}
