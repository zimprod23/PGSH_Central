using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// What one import covers, and what it does about the students it does not name.
/// </summary>
/// <remarks>
/// <para><b>An omitted <see cref="LevelId"/> genuinely means every promotion of the year</b>, and that
/// is not the widening-on-absence defect CLAUDE.md warns about: the <em>year</em> is still resolved to
/// exactly one — the current one when <see cref="AcademicYearId"/> is omitted — and it is the year, not
/// the level, that makes an identifier unambiguous. A student holds at most one registration per
/// academic year (unique index), so matching a CNE across every level of one year is exactly as safe as
/// matching it within one promotion. Matching across <em>years</em> is what turns a legitimate row into
/// an ambiguous one, and that is still impossible here.</para>
///
/// <para><b><see cref="DefaultUnlistedToAdmis"/> inverts what the file is.</b> Off, the sheet is the
/// promotion and silence is silence — a registration no row mentions keeps whatever status it has. On,
/// the sheet is the list of <em>exceptions</em> and silence is a verdict: everyone not named is admis,
/// or left alone where the year may be their last. That is how a PV is actually written, and
/// it turns a 5,000-row file into a 50-row one — but it means an omission promotes someone, which is
/// why the apply will not run without <see cref="ApplyDeliberationCommand.ConfirmedDefaultCount"/>.</para>
/// </remarks>
public sealed record DeliberationScope(
    int? LevelId,
    int? AcademicYearId,
    bool DefaultUnlistedToAdmis);

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
    string? LevelLabel,
    DeliberationRowStatus Status,
    RegistrationStatus? Outcome,
    string Message,
    bool HasUnvalidatedStages);

/// <summary>
/// What the import does to one promotion, so the confirmation can be read promotion by promotion
/// rather than as a single total. Bounded by construction — one entry per level of the year.
/// </summary>
public sealed record DeliberationLevelBreakdown(
    int LevelId,
    string LevelLabel,
    int Registrations,
    // Named in the uploaded file, whatever the decision.
    int Listed,
    // Not named, and the default writes « Admis » on them.
    int WillPromote,
    // Not named, and possibly in their last year — so nothing is written. See the planner: the default
    // promotes but never graduates, because a final year is where lingering is ordinary.
    int FinalYearUndecided,
    // Not named, and already carrying a verdict: left exactly as they are.
    int AlreadyDecided);

/// <summary>
/// The dry run, and — after a successful apply — the record of what was written. Same shape both
/// times because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
public sealed record DeliberationReport(
    string AcademicYearLabel,
    string ScopeLabel,
    bool DefaultsApplied,
    int TotalRows,
    int WillRecord,
    int WillReplace,
    int ErrorCount,
    // Rows whose verdict PGSH's own stage record does not obviously support. Never blocking.
    int ContradictionCount,
    // Registrations of the scope that no row of the sheet mentions. Without defaults they keep
    // whatever status they have, and a promotion of 688 closed with a 200-row file is worth seeing
    // before applying. With defaults, this is the number that gets a verdict from silence.
    int NotCovered,
    // The subset of NotCovered the default actually writes — all of them « Admis ».
    int DefaultedCount,
    // Not named and possibly in their last year: left untouched, and the faculty names its graduates.
    int FinalYearUndecidedCount,
    // Not named and already carrying a verdict — the default never overwrites one.
    int AlreadyDecidedCount,
    // Not named and not a year of study at all (« Retrait » and its kind), so nothing to promote.
    int NotAPromotionCount,
    bool CanApply,
    IReadOnlyDictionary<string, int> OutcomeCounts,
    IReadOnlyList<DeliberationLevelBreakdown> ByLevel,
    IReadOnlyList<DeliberationRowReport> Rows,
    // Rows is capped: a year-wide file is whatever the user uploaded, and the counts above stay exact.
    bool RowsTruncated);

/// <summary>How the blank canvas is shaped.</summary>
public enum DeliberationTemplateMode
{
    /// <summary>
    /// One line per student, decision blank or pre-filled with the verdict already recorded. The whole
    /// promotion is stated explicitly and nothing is implied by absence.
    /// </summary>
    Full,

    /// <summary>
    /// An empty decision sheet plus a reference list of the students. The jury writes only the
    /// exceptions; everyone left out is admis. Pairs with <see cref="DeliberationScope.DefaultUnlistedToAdmis"/>.
    /// </summary>
    Exceptions,
}

/// <summary>
/// A promotion's own students, used to generate a pre-filled sheet. Nobody should have to hand-build
/// the columns or retype an identifier — a mistyped CNE is a row that silently belongs to no one.
/// </summary>
public sealed record DeliberationTemplate(
    string AcademicYearLabel,
    string ScopeLabel,
    DeliberationTemplateMode Mode,
    IReadOnlyList<DeliberationTemplateStudent> Students);

public sealed record DeliberationTemplateStudent(
    string? Cne, string Appogee, string FullName, string LevelLabel, string GroupLabel, string? CurrentDecision);

/// <summary>
/// Reads an uploaded déliberation sheet and writes the blank one. The port lives here so the
/// application layer never learns what .xlsx is; the ClosedXML adapter sits in Infrastructure.
/// </summary>
public interface IDeliberationSheetParser
{
    /// <summary>Reads every data row. Cell-level mistakes are carried through as null/raw values for
    /// the planner to report, not thrown.</summary>
    IReadOnlyList<DeliberationRow> Parse(Stream sheet);

    /// <summary>Builds the workbook scolarité downloads before the jury sits.</summary>
    byte[] BuildTemplate(DeliberationTemplate template);
}
