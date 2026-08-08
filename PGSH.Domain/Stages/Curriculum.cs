using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using AppResult = PGSH.SharedKernel.Result;

namespace PGSH.Domain.Stages;

/// <summary>
/// What one <see cref="CnpnVersion"/> requires of one level: the exact set of stages, with the weight
/// and duration that text gives them.
///
/// <para>
/// This is deliberately <em>not</em> a validity window on <see cref="Stage"/>. A window says "this
/// stage is valid from X to Y", which forces someone to know when it ends — and nobody does: the CNPN
/// is reissued whenever the ministry decides, and a stage dropped by one text can reappear in a later
/// one. A per-version requirement set predicts nothing. You record what each published text actually
/// says; a stage leaving is simply its absence from the new set, and its return is its presence in a
/// later one.
/// </para>
///
/// <para>
/// <b>Keyed on (<see cref="CnpnVersionId"/>, <see cref="LevelId"/>), not on the academic year.</b>
/// It was keyed on the year until arrêté 1650.25 made that impossible: from 2026-2027 a single
/// (level, year) cell holds students of two different texts — those arriving on schedule under the
/// six-year CNPN and those repeating under the seven-year one. The year is when a thing happens; the
/// version is which rules apply, and only the second decides what a student owes. See
/// <see cref="CnpnVersion"/> for why assignment follows the student's entry rather than the calendar.
/// </para>
///
/// <para>
/// <see cref="Stage"/> stays the timeless catalogue entry so historical <c>InternshipAssignment</c>s
/// keep pointing at a stable identity; what differs between texts lives here.
/// </para>
/// </summary>
public sealed class Curriculum : Entity
{
    public int Id { get; set; }

    public int LevelId { get; set; }
    public Level Level { get; set; } = default!;

    public int CnpnVersionId { get; set; }
    public CnpnVersion CnpnVersion { get; set; } = default!;

    /// <summary>Free-text reference to the ministerial text this set was taken from, when known.</summary>
    public string? Reference { get; set; }

    public ICollection<CurriculumStage> Stages { get; set; } = new List<CurriculumStage>();

    /// <summary>
    /// Adds a stage requirement. <paramref name="coefficient"/> and <paramref name="durationInDays"/>
    /// are recorded per text because a CNPN can keep a stage and reweight it.
    /// </summary>
    public Result AddStage(int stageId, int coefficient, int durationInDays)
    {
        if (coefficient < 1)
            return AppResult.Failure(CurriculumErrors.InvalidCoefficient);

        if (durationInDays < 1)
            return AppResult.Failure(CurriculumErrors.InvalidDuration);

        if (Stages.Any(s => s.StageId == stageId))
            return AppResult.Failure(CurriculumErrors.StageAlreadyRequired(stageId));

        // No pre-set Id: on an already-tracked curriculum a store-generated key makes EF classify the
        // child Modified instead of Added. Same gotcha as InternshipAssignment.Delocalize.
        Stages.Add(new CurriculumStage
        {
            CurriculumId   = Id,
            StageId        = stageId,
            Coefficient    = coefficient,
            DurationInDays = durationInDays,
        });

        return AppResult.Success();
    }

    /// <summary>
    /// Drops a stage from this text's requirements. Students who failed it under an earlier CNPN keep
    /// owing it — the obligation is theirs, not the text's — so the removal is announced rather than
    /// silently applied, and the administration settles each case by hand.
    /// </summary>
    public Result RemoveStage(int stageId)
    {
        var entry = Stages.FirstOrDefault(s => s.StageId == stageId);
        if (entry is null)
            return AppResult.Failure(CurriculumErrors.StageNotRequired(stageId));

        Stages.Remove(entry);
        Raise(new CurriculumStageRemovedDomainEvent(Id, LevelId, CnpnVersionId, stageId));
        return AppResult.Success();
    }

    /// <summary>
    /// Seeds this text's set from another's. A new CNPN mostly repeats the previous one with a change
    /// or two, so cloning then editing is the realistic flow — and it keeps each version an
    /// independent record rather than a diff nobody can read back.
    /// </summary>
    public Result CopyFrom(Curriculum source)
    {
        if (source.LevelId != LevelId)
            return AppResult.Failure(CurriculumErrors.LevelMismatch);

        if (Stages.Count > 0)
            return AppResult.Failure(CurriculumErrors.NotEmpty);

        foreach (var entry in source.Stages)
        {
            var result = AddStage(entry.StageId, entry.Coefficient, entry.DurationInDays);
            if (result.IsFailure) return result;
        }

        return AppResult.Success();
    }

    public bool Requires(int stageId) => Stages.Any(s => s.StageId == stageId);
}

/// <summary>One stage required by a <see cref="Curriculum"/>, with that year's weight and duration.</summary>
public sealed class CurriculumStage
{
    public int Id { get; set; }

    public int CurriculumId { get; set; }
    public Curriculum Curriculum { get; set; } = default!;

    public int StageId { get; set; }
    public Stage Stage { get; set; } = default!;

    /// <summary>The weight this CNPN gave the stage — a text may keep a stage but reweight it.</summary>
    public int Coefficient { get; set; } = 1;

    public int DurationInDays { get; set; } = 30;
}
