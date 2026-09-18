using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;

namespace PGSH.Application.Stages.Curricula.Compare;

/// <summary>
/// What changed in a level's CNPN between two texts. This is the read behind manual revalidation: a
/// student who failed a stage under the old CNPN is judged against it, but can only be re-planned
/// against today's — so the administration needs both side by side before deciding.
/// </summary>
public sealed record CompareCurriculaQuery(int LevelId, int? FromCnpnVersionId, int? ToCnpnVersionId)
    : IQuery<CurriculumComparisonResponse>;

/// <summary>
/// ⚠ Two texts, two sentences. A comparison against a single text is not a narrower comparison — it
/// is no comparison — and bound non-nullable these threw in routing, so leaving either selector empty
/// produced a bare 400 rather than a line saying which of the two is missing.
/// </summary>
internal sealed class CompareCurriculaQueryValidator : AbstractValidator<CompareCurriculaQuery>
{
    public CompareCurriculaQueryValidator()
    {
        RuleFor(x => x.FromCnpnVersionId).IsARequiredReference(
            "Le texte de départ est obligatoire : c'est celui sous lequel l'étudiant a échoué, et "
            + "c'est lui qui juge ce qu'il doit.");

        RuleFor(x => x.ToCnpnVersionId).IsARequiredReference(
            "Le texte d'arrivée est obligatoire : c'est celui contre lequel la re-planification est "
            + "faite aujourd'hui.");
    }
}

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
