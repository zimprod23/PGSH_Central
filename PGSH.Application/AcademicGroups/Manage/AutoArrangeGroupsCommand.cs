using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage;

/// <summary>
/// Cuts a promotion's unassigned students into rosters.
/// </summary>
/// <remarks>
/// ⚠ <b>Audited because this is the act that makes rosters exist.</b> <c>DeleteGroupCommand</c> was
/// recorded and this was not, so a promotion could acquire a hundred rosters with nothing anywhere
/// saying who asked for them — which is what happened on the 7ᵉ MED on 02/09/2026, and what could
/// not be answered afterwards.
/// </remarks>
/// <param name="GroupSize">
/// The <b>maximum</b> students per roster. ⚠ Exactly one of this and <paramref name="GroupCount"/> is
/// given — the validator refuses both and neither.
/// </param>
/// <param name="GroupCount">
/// How many rosters to make — « la 5ᵉ MED en 100 groupes ». The faculty thinks in this unit as often
/// as in the other, and until now only the size could be asked for.
/// </param>
public sealed record AutoArrangeGroupsCommand(
    int LevelId,
    int AcademicYearId,
    int? GroupSize = null,
    int? GroupCount = null) : ICommand<BulkResponse<Guid, int>>, IAuditableCommand
{
    /// <summary>Which unit the operator used. Both are a cut; they differ in what is held fixed.</summary>
    public bool IsByCount => GroupCount is > 0;

    public string AuditAction => "GROUPS_AUTO_ARRANGED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();

    /// <summary>
    /// ⚠ The unit travels with the number. « 12 » means two different acts depending on which field
    /// carried it, and a register that recorded only the figure could not tell them apart.
    /// </summary>
    public string? AuditMetadata => AuditMetadataJson.Of(
        ("levelId", LevelId),
        ("askedBy", IsByCount ? "count" : "size"),
        ("groupSize", GroupSize),
        ("groupCount", GroupCount));
}