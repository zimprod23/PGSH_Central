using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// One line of an uploaded déliberation sheet, exactly as it was typed. Values stay raw — the
/// decision is carried as text and the identifiers untrimmed — so a mistyped cell is reported
/// against its own row instead of failing the whole parse.
/// </summary>
public sealed record DeliberationRow(
    int SheetRow,
    string? Cne,
    string? Appogee,
    string? Decision,
    string? Motif);

/// <summary>What will happen to a row — or why nothing can.</summary>
public enum DeliberationRowStatus
{
    /// <summary>No verdict on record yet; the import records one.</summary>
    WillRecord,

    /// <summary>A verdict is already on record; the import replaces it. A jury correcting itself is
    /// a requirement, not an error.</summary>
    WillReplace,

    NoIdentifier,
    UnknownStudent,
    NotInPromotion,
    DuplicateStudent,
    MissingDecision,
    InvalidDecision,

    /// <summary>« Diplômé » on a level that is not the last year of the student's CNPN.</summary>
    NotAFinalYear,
}

public static class DeliberationRowStatusExtensions
{
    /// <summary>A row that cannot be applied. One of these anywhere refuses the whole import.</summary>
    public static bool IsError(this DeliberationRowStatus status) =>
        status is not (DeliberationRowStatus.WillRecord or DeliberationRowStatus.WillReplace);
}

/// <summary>
/// What one row will do, plus what is <em>odd</em> about it.
/// </summary>
/// <remarks>
/// <see cref="HasUnvalidatedStages"/> is deliberately not an error. The faculty deliberates on the
/// whole year — subjects, TP, exams — and PGSH sees only the stages, so a student admitted with a
/// stage still unmarked is perfectly ordinary; with 0 authored periods in the base it is currently
/// the norm. It is surfaced because a jury reading its own file back wants to see it, and buried
/// nowhere else. Reporting rather than resolving is the same choice as
/// <c>EntryPredatesText</c> in the CNPN targeting.
/// </remarks>
public sealed record DeliberationRowReport(
    int SheetRow,
    string? Cne,
    string? Appogee,
    string? StudentFullName,
    DeliberationRowStatus Status,
    RegistrationStatus? Outcome,
    string Message,
    bool HasUnvalidatedStages);

/// <summary>
/// The dry run, and — after a successful apply — the record of what was written. Same shape both
/// times because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
public sealed record DeliberationReport(
    string AcademicYearLabel,
    string LevelLabel,
    int TotalRows,
    int WillRecord,
    int WillReplace,
    int ErrorCount,
    // Rows whose verdict PGSH's own stage record does not obviously support. Never blocking.
    int ContradictionCount,
    // Registrations of the promotion that no row of the sheet mentions — they keep whatever status
    // they have. A promotion of 688 closed with a 200-row file is worth seeing before applying.
    int NotCovered,
    bool CanApply,
    IReadOnlyDictionary<string, int> OutcomeCounts,
    IReadOnlyList<DeliberationRowReport> Rows);

/// <summary>
/// A promotion's own students, used to generate a pre-filled sheet. Nobody should have to hand-build
/// the columns or retype an identifier — a mistyped CNE is a row that silently belongs to no one.
/// </summary>
public sealed record DeliberationTemplate(
    string AcademicYearLabel,
    string LevelLabel,
    IReadOnlyList<DeliberationTemplateStudent> Students);

public sealed record DeliberationTemplateStudent(
    string Cne, string Appogee, string FullName, string GroupLabel, string? CurrentDecision);

/// <summary>
/// Reads an uploaded déliberation sheet and writes the blank one. The port lives here so the
/// application layer never learns what .xlsx is; the ClosedXML adapter sits in Infrastructure.
/// </summary>
public interface IDeliberationSheetParser
{
    /// <summary>Reads every data row. Cell-level mistakes are carried through as null/raw values for
    /// the planner to report, not thrown.</summary>
    IReadOnlyList<DeliberationRow> Parse(Stream sheet);

    /// <summary>Builds the pre-filled workbook scolarité downloads before the jury sits.</summary>
    byte[] BuildTemplate(DeliberationTemplate template);
}
