namespace PGSH.Domain.Stages;

/// <summary>
/// How far down a <see cref="CnpnVersion"/>'s span is already spoken for — the deepest study year it
/// carries requirements for, and the deepest one it has been declared to take effect for. A text
/// cannot be shortened below either.
///
/// <para><b>Why the text is handed this rather than reading its own children.</b> An un-Included
/// collection is indistinguishable from an empty one, so an aggregate that counts its own
/// <c>Curricula</c> answers « rien enregistré » whenever a caller forgot to load them — and this
/// rule has no unique index behind it, so the shortening would go through and strand every
/// requirement set below the new span, silently. Worse, it cannot be caught here: the in-memory
/// provider fixes navigations up from the change tracker, so a forgotten <c>Include</c> passes the
/// whole suite and fails only against PostgreSQL. The store is asked for the floor; the text decides
/// what to do about it.</para>
///
/// <para>Same division as <c>AcademicYear.OverlapsWith</c>, which is handed the other year's dates
/// rather than going looking for the other years. Two adjacent <c>int</c>s are a record struct and
/// not two parameters for the ordinary reason: swapped, they would produce a plausible refusal
/// naming the wrong rule.</para>
/// </summary>
public readonly record struct CnpnSpanFloor(int DeepestRecordedLevelYear, int DeepestGoverningLevelYear)
{
    /// <summary>A text nothing hangs off yet — free to be shortened to any valid span.</summary>
    public static readonly CnpnSpanFloor None = new(0, 0);
}
