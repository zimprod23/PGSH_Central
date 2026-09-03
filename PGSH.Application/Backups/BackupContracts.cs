using PGSH.Domain.Backups;

namespace PGSH.Application.Backups;

/// <summary>One safe point, as a screen reads it.</summary>
/// <remarks>
/// ⚠ <paramref name="SchemaMatchesRunning"/> is <b>sent</b>, never re-derived on the client — the same
/// rule as <c>ServicePeriodResponse.State</c> and <c>RegistrationHoldResponse.BlocksPlanning</c>. The
/// comparison is <see cref="SchemaFingerprint.MatchesSchemaOf"/>'s, and a second copy of it in
/// TypeScript is one rule on two sides of a network boundary with nothing able to catch them
/// disagreeing.
/// </remarks>
public sealed record BackupPointResponse(
    string Id,
    string Label,
    BackupKind Kind,
    DateTime TakenAtUtc,
    long SizeBytes,
    string? LastMigration,
    string? GitSha,
    string? Note,
    string? TakenBy,
    BackupVerification Verification,
    DateTime? VerifiedAtUtc,
    bool SchemaMatchesRunning,
    IReadOnlyList<CensusLine> Census);

public sealed record CensusLine(string Table, long? Count);

/// <summary>
/// The banner every irreversible act shows before it lets somebody through.
/// </summary>
/// <remarks>
/// ⚠ <paramref name="UnavailableReason"/> is set exactly when the state is
/// <see cref="SafePointState.Unavailable"/>, and it is why the screen can say « Docker ne répond pas »
/// instead of « aucune sauvegarde » — two states that call for opposite acts.
///
/// <para>⚠ <paramref name="KeycloakRealmCovered"/> is false today and the page says so out loud.
/// Restoring the base without the matching realm leaves <c>SyncUserMiddleware</c> matching a Keycloak
/// <c>sub</c> against <c>User</c> rows that no longer exist — and its fallback is the e-mail address,
/// which is how somebody lands in another person's account. An uncovered second volume that nobody is
/// told about is worse than one nobody has automated.</para>
/// </remarks>
public sealed record SafePointStatusResponse(
    SafePointState State,
    string Location,
    string? UnavailableReason,
    BackupPointResponse? Latest,
    long? AgeMinutes,
    bool HasUsableUndo,
    string? RunningMigration,
    string? RunningGitSha,
    int TotalPoints,
    DateTime? NextScheduledAtUtc,
    bool KeycloakRealmCovered);

/// <summary>
/// What restoring one point would cost, and the command that does it.
/// </summary>
/// <remarks>
/// ⚠ It is a <em>read</em>, and it does not fail on a schema mismatch. §18 asks the restore to refuse
/// loudly on one — but a refusal that never shows the plan cannot tell the operator <em>which</em>
/// <c>dotnet ef database update</c> makes the point usable again. So the plan is always returned, it
/// states <see cref="SchemaMatchesRunning"/> plainly, and it is the restore itself that stops.
/// </remarks>
public sealed record RestorePlanResponse(
    BackupPointResponse Point,
    bool SchemaMatchesRunning,
    string? RunningMigration,
    string? SchemaStepCommand,
    string RestoreCommand,
    IReadOnlyList<RestoreImpactLine> Impact,
    long? TotalRowsDiscarded,
    long? TotalRowsRestored,
    string ConfirmationPhrase);

/// <summary>
/// ⚠ <paramref name="Discarded"/> and <paramref name="Restored"/> are both null when the point
/// predates this table being censused — « ce point n'en dit rien », which is not zero.
/// </summary>
public sealed record RestoreImpactLine(
    string Table,
    long? AtSafePoint,
    long? Now,
    long? Discarded,
    long? Restored);
