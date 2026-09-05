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
public sealed record AutoArrangeGroupsCommand(
    int LevelId,
    int AcademicYearId,
    int GroupSize) : ICommand<BulkResponse<Guid, int>>, IAuditableCommand
{
    public string AuditAction => "GROUPS_AUTO_ARRANGED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();
    public string? AuditMetadata => AuditMetadataJson.Of(
        ("levelId", LevelId),
        ("groupSize", GroupSize));
}