using PGSH.Domain.Students;

namespace PGSH.Domain.Registrations;

/// <summary>
/// The faculty allowing one student into the final year of his cursus while he still owes a stage
/// from an earlier one.
///
/// <para><b>Why the exception has to be a row rather than a flag.</b> The rule is that the last year
/// cannot begin until everything below it is validated — a 7ᵉ année under arrêté 2174.18, a 6ᵉ under
/// 1650.25. It is a real rule and the réinscription enforces it. But the faculty has always granted
/// exceptions, and a system that cannot express one is a system people work around in SQL, which is
/// worse than one that records who decided and why.</para>
///
/// <para><b>Keyed on (student, academic year)</b>, not on the registration: the registration it
/// permits does not exist yet — the whole point is that it is refused without this. It is granted
/// before the rollover and consumed by it.</para>
///
/// <para><b>What was owed is captured at the moment of granting</b>
/// (<see cref="OutstandingAtGrant"/>, <see cref="OutstandingSummary"/>). A waiver read back two years
/// later must say what it actually excused — by then the stage may have been revalidated, dropped by
/// a new CNPN, or served under another registration, and « on lui a permis de passer » with nothing
/// attached is not a record anyone can audit.</para>
/// </summary>
public sealed class FinalYearEntryWaiver
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;

    /// <summary>The year the student is being allowed to start his final year in.</summary>
    public int AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = default!;

    /// <summary>Required. An exception nobody justified is indistinguishable from a bug.</summary>
    public string Reason { get; set; } = default!;

    /// <summary>How many stages were outstanding when the waiver was granted.</summary>
    public int OutstandingAtGrant { get; set; }

    /// <summary>Which ones, as they read at the time — e.g. « Cardiologie (3ème année), Pédiatrie (4ème année) ».</summary>
    public string? OutstandingSummary { get; set; }

    /// <summary>The local user who granted it, when known.</summary>
    public Guid? GrantedByUserId { get; set; }

    public DateTime GrantedOn { get; set; }
}
