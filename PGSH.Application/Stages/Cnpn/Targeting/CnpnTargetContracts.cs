using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.Cnpn.Targeting;

/// <summary>
/// Who a CNPN binds, written as a rule rather than inferred.
///
/// <para>An arrêté states its own scope — 1650.25 art. 2 excludes everyone registered before
/// 2024-2025 — and the shape of that statement varies from text to text. Hard-coding one reading
/// (as the first implementation did, keying solely on entry year) fits the text in hand and nothing
/// else. Authoring it puts the faculty in charge of the reading, and makes the population visible
/// before it is committed.</para>
///
/// <para><b>This selects people who already exist.</b> Future intakes are covered by the standing
/// rule on the version itself (<c>AppliesToEntrantsFromAcademicYearId</c>), which stamps each new
/// registration as it arrives. A selector cannot catch a student who has not registered yet, so a
/// text needs both halves — a frozen membership for today's students and a standing rule for
/// tomorrow's.</para>
/// </summary>
public sealed record CnpnTargetCriteria(
    AcademicProgram Program,

    /// <summary>"…et en dessous": every level of the programme at or below this study year.</summary>
    int MaxLevelYear,

    /// <summary>
    /// The year whose registrations are read. Null resolves to the current year. It anchors *which*
    /// students the rule sees; it never influences which text they end up under.
    /// </summary>
    int? AsOfAcademicYearId = null,

    /// <summary>
    /// Whether to include students the rule catches but whose first registration predates the text's
    /// own intake year. Defaults to excluding them, because the text usually says so in as many
    /// words — but it is the faculty's call, not the system's, so it is asked rather than assumed.
    /// </summary>
    bool IncludeEntryContradictions = false);

/// <summary>What the rule would do to one student — or why it would leave them alone.</summary>
public enum CnpnTargetRowStatus
{
    /// <summary>No stamp, or one that may be replaced (unset, or previously inferred).</summary>
    WillAssign,

    /// <summary>Already confirmed under this very text; nothing to do.</summary>
    AlreadyOnThisText,

    /// <summary>
    /// Caught by the rule, but their first registration predates the text's intake year — the case
    /// where "année ≤ N" and the arrêté's own wording disagree, which is the repeater. Assigned only
    /// when <see cref="CnpnTargetCriteria.IncludeEntryContradictions"/> says so.
    /// </summary>
    EntryPredatesText,

    /// <summary>
    /// Confirmed under a different text. Never overwritten in bulk: moving a student between texts
    /// changes how many years they owe, so it stays a deliberate, per-student act.
    /// </summary>
    ConfirmedOnAnotherText,
}

public sealed record CnpnTargetRow(
    Guid    StudentId,
    string  FullName,
    string? Cne,
    string? LevelLabel,
    string? CurrentCnpnCode,
    string? EntryYearLabel,
    CnpnTargetRowStatus Status,
    string  Message);

/// <summary>
/// The dry run, and — after an apply — the record of what was written. Same shape both times because
/// it is the same plan: what the preview showed is literally what runs.
/// </summary>
public sealed record CnpnTargetPreview(
    int     CnpnVersionId,
    string  CnpnVersionCode,
    string  CnpnVersionLabel,
    string  AsOfYearLabel,
    int     TotalMatched,
    int     WillAssign,
    int     AlreadyOnThisText,
    int     EntryPredatesText,
    int     ConfirmedOnAnotherText,
    bool    CanApply,

    /// <summary>
    /// Only the rows a human has to look at — the contradictions and the conflicts — capped so a rule
    /// matching two thousand students does not return two thousand rows. The counts above are always
    /// the whole truth; this is the sample you review.
    /// </summary>
    IReadOnlyList<CnpnTargetRow> NeedsAttention,
    int     NeedsAttentionTotal);
