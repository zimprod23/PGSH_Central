namespace PGSH.Domain.Registrations;

/// <summary>
/// A registration PGSH has created but will not plan, until somebody settles what is wrong with it.
/// </summary>
/// <remarks>
/// <para><b>Why the flexibility has to be a row and not a refusal.</b> The faculty's réinscription
/// roll states where every returning student goes, and PGSH's own record of what those students owe
/// is behind it — the stages were served, the évaluations were not keyed in. Refusing the row loses
/// the faculty's statement; applying it silently loses ours. The hold keeps both: the registration
/// exists, the promotion is complete on paper, and the 232 rows somebody has to look at are a
/// worklist rather than a diff between a spreadsheet and a database.</para>
///
/// <para><b>What it costs the student is planning, and only planning.</b> A held registration is not
/// cut into a roster, is given no cohort affectation, and receives no published période — see
/// <see cref="RegistrationHoldPolicy"/> for the single predicate every planning read shares. It is
/// not a status, it does not annul anything, and it removes nothing that already exists: taking
/// périodes away is <c>UnpublishCohortScheduleCommand</c>'s act, which names what it destroys and
/// asks twice. A hold only stops <em>new</em> work being built on a registration nobody has
/// confirmed.</para>
///
/// <para><b>The evidence is a snapshot, exactly as <see cref="FinalYearEntryWaiver"/>'s is.</b> Read
/// back in March, « il était signalé » says nothing: by then the stage may have been revalidated,
/// dropped by a new text, or served under another registration. <see cref="Evidence"/> is what was
/// true at the moment the hold was raised, in the sentence the operator was shown.</para>
///
/// <para>⚠ <b>Released by hand, never by the condition lapsing.</b> The point of the hold is that a
/// human decides — a registration that quietly re-entered the répartition the day an évaluation was
/// keyed in would be exactly the silent behaviour this exists to remove. <see cref="ReleasedOn"/>
/// and <see cref="ReleaseNote"/> record that decision; the row is kept, not deleted, so the file can
/// still say the student was flagged and who cleared him.</para>
/// </remarks>
public sealed class RegistrationHold
{
    public Guid Id { get; set; }

    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; } = default!;

    public RegistrationHoldReason Reason { get; set; }

    /// <summary>
    /// What was true when the hold was raised — « 2 stage(s) antérieur(s) non validés : Cardiologie
    /// (3ᵉ année), Pédiatrie (4ᵉ année) ». Required: a flag that cannot say what it saw is one
    /// nobody can act on, and it is the sentence the export prints.
    /// </summary>
    public string Evidence { get; set; } = default!;

    public DateTime RaisedOn { get; set; }

    /// <summary>The act that raised it, when one user did — null for a bulk roll applied by a job.</summary>
    public Guid? RaisedByUserId { get; set; }

    /// <summary>Null while the hold stands. Set once, by <see cref="Registration.ReleaseHold"/>.</summary>
    public DateTime? ReleasedOn { get; set; }

    public Guid? ReleasedByUserId { get; set; }

    /// <summary>
    /// Why it was cleared — « évaluations saisies, tous les stages antérieurs validés ». Required on
    /// release for the same reason <see cref="Evidence"/> is required on raising it.
    /// </summary>
    public string? ReleaseNote { get; set; }

    /// <summary>Whether this hold still holds. The <em>only</em> reading of that; see
    /// <see cref="RegistrationHoldPolicy"/> for the query-side form.</summary>
    public bool IsActive => ReleasedOn is null;
}
