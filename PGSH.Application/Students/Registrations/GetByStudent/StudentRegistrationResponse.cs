namespace PGSH.Application.Students.Registrations.GetByStudent;

/// <summary>
/// One year of a student's enrolment, as the admin dossier and the student portal show it.
/// </summary>
/// <param name="OutcomeSource">
/// Whether <paramref name="Status"/> is a verdict the faculty declared or one PGSH deduced, and null
/// while the year is still running. ⚠ Without it the screen cannot tell « Admis, prononcé le 12 juillet »
/// from a status somebody typed into a form — and the réinscription, which reads exactly this field,
/// would silently disagree with what the dossier shows.
/// </param>
/// <param name="AcademicGroupId">
/// The roster this registration sits in, or null for a student nobody has placed yet. Null is what the
/// dossier offers « Affecter à un groupe » on; a value is what makes it a transfer instead.
/// </param>
/// <param name="CnpnCode">
/// The CNPN that governed this year, which is <b>not</b> necessarily the one the student follows
/// today. It is what he was required to do then, so it is what an outstanding stage from that year is
/// still measured against — and on the parcours it is the only thing that explains why two years of
/// one student can owe different sets. Falls back to the student's own stamp for the imported years
/// the backfill could not reach, and is null when nothing is known.
/// </param>
/// <param name="CnpnSource">
/// How that text was decided — <c>Effectivity</c> means an authored rule moved him onto it, and it is
/// the one value that says his text changed mid-cursus rather than being carried from his intake.
/// </param>
public sealed record StudentRegistrationResponse(
    Guid Id,
    int AcademicYearId,
    string AcademicYear,
    int LevelId,
    string? LevelLabel,
    string Status,
    bool HasFailures,
    string? FailureDescription,
    string? OutcomeSource,
    DateTime? OutcomeRecordedOn,
    int? AcademicGroupId,
    string? AcademicGroupLabel,
    int? CnpnVersionId,
    string? CnpnCode,
    string? CnpnSource);
