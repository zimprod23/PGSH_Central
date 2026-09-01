namespace PGSH.Domain.Registrations;

/// <summary>
/// The study a student did somewhere else before the registration that admitted him here — the
/// équivalence, as the faculty pronounced it.
///
/// <para><b>Keyed on the registration that let him in</b>, one row at most, because that is the act
/// it justifies. A student who arrives in 3ᵉ année has exactly one entry into this faculty; the
/// registrations that follow are ours and record nothing about elsewhere.</para>
///
/// <para><b>Why this cannot be left implicit.</b> Today a transfer owes nothing:
/// <c>OutstandingStageFinder</c> reads « owed » as <em>every attempt came back NonValidé</em>, and a
/// student with no attempt at all has no failed one — so <c>FinalYearGuard</c> stands aside and
/// nothing objects. That is the right reading of our own record, but it holds only while « owed » is
/// defined negatively. The moment it widens to the CNPN's requirement set — which is the stated plan
/// once 1650.25's sets are entered — a student transferred into 5ᵉ année owes <em>every stage of the
/// four years he did elsewhere</em>, and there is nothing on record to say otherwise. That is the day
/// this row has to already exist: it cannot be reconstructed afterwards from anything PGSH holds.
/// <see cref="LastLevelYearCompleted"/> is the boundary below which the widening must not look.</para>
///
/// <para><b>No stages are invented.</b> Materialising validated <c>InternshipAssignment</c>s for the
/// years done elsewhere would make the dossier look complete at the price of rows nobody served —
/// which every count, every mean, every chef worklist and every occupancy figure would then have to
/// learn to exclude. The equivalence is one fact stated once, in the place that states it.</para>
///
/// <para>Same shape as <c>FinalYearEntryWaiver</c>, and for the same reason: a required reference and
/// a snapshot of what was recognised. A decision that cannot say what it recognised is not a record.</para>
/// </summary>
public sealed class PriorEnrolment
{
    public Guid Id { get; set; }

    /// <summary>The registration this student entered the faculty on.</summary>
    public Guid RegistrationId { get; set; }
    public Registration Registration { get; set; } = default!;

    /// <summary>Required. Free text — PGSH is not the register of the world's faculties.</summary>
    public string Institution { get; set; } = default!;

    /// <summary>Null means Morocco; it is only worth recording when it is not.</summary>
    public string? Country { get; set; }

    /// <summary>
    /// The last year of study completed there, as a level year — 2 for a student entering our 3ᵉ
    /// année. This is the number the équivalence actually pronounces, and the only one that survives
    /// a change of curriculum on either side.
    /// </summary>
    public int LastLevelYearCompleted { get; set; }

    /// <summary>Required. The arrêté, the PV or the décision d'équivalence this rests on.</summary>
    public string EquivalenceReference { get; set; } = default!;

    public DateOnly? EquivalenceDate { get; set; }

    public string? Note { get; set; }

    /// <summary>The local user who recorded it, when known.</summary>
    public Guid? RecordedByUserId { get; set; }

    public DateTime RecordedOn { get; set; }
}
