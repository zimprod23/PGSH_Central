namespace PGSH.Domain.Backups;

/// <summary>
/// How many rows the tables that matter held at a moment in time — the thing a restore asserts, and
/// the thing a restore's cost is measured against.
/// </summary>
/// <remarks>
/// Kept as name → count rather than as typed properties on purpose: a manifest is a <em>document</em>
/// read back months later, possibly by a build that knows about tables the writer did not. A missing
/// key then reads as « ce point n'en dit rien », where a typed property would read as zero — which is
/// the difference between silence and a false statement, and the whole reason
/// <see cref="Compare"/> returns <see cref="CensusDelta.Unknown"/> instead of a number.
///
/// <para>⚠ The list is deliberately short. It is not an inventory of the schema; it is the set of
/// tables whose count somebody would recognise as wrong — which is what makes
/// « la restauration effacerait 6 813 inscriptions » a sentence an operator can act on.</para>
/// </remarks>
public sealed record DatabaseCensus(IReadOnlyDictionary<string, long> Counts)
{
    /// <summary>The tables a census covers, in the order they are printed.</summary>
    public static readonly IReadOnlyList<string> Tables =
    [
        "Students",
        "Registrations",
        "InternshipAssignments",
        "ServicePeriods",
        "ServiceEvaluations",
        "AcademicGroups",
        "Cohorts",
        "StageSlots",
        "CohortSlotAssignments",
        "RegistrationHolds",
        "Holidays",
        "AuditLogs",
    ];

    public static readonly DatabaseCensus Empty = new(new Dictionary<string, long>());

    public long? this[string table] =>
        Counts.TryGetValue(table, out long value) ? value : null;

    /// <summary>
    /// What has happened to each table since <paramref name="taken"/> was recorded, from the point of
    /// view of the base as it stands now. <c>Written</c> is what a restore would discard;
    /// <c>Removed</c> is what it would bring back.
    /// </summary>
    public static IReadOnlyList<CensusDelta> Compare(DatabaseCensus taken, DatabaseCensus current) =>
        Tables
            .Select(table => new CensusDelta(table, taken[table], current[table]))
            .ToList();
}

/// <summary>One table's story between a safe point and now.</summary>
public sealed record CensusDelta(string Table, long? AtSafePoint, long? Now)
{
    public static CensusDelta Unknown(string table) => new(table, null, null);

    /// <summary>Neither side can be compared — the point predates this table being censused.</summary>
    public bool IsUnknown => AtSafePoint is null || Now is null;

    /// <summary>Rows written since the point. A restore discards these.</summary>
    public long? Written => IsUnknown ? null : Math.Max(0, Now!.Value - AtSafePoint!.Value);

    /// <summary>Rows gone since the point. A restore brings these back.</summary>
    public long? Removed => IsUnknown ? null : Math.Max(0, AtSafePoint!.Value - Now!.Value);
}
