namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>
/// One line of the canevas des affectations, exactly as it was typed. One <b>période</b>, not one
/// stage: a rotation that crosses three services is three lines carrying the same student and the
/// same stage, and it is the planner that folds them back into one affectation.
/// </summary>
/// <remarks>
/// <para>The canvas is PGSH's own document — <c>GetAffectationSheetTemplateQuery</c> hands it out
/// pre-filled with the promotion's students, and its « Périodes » columns are the ones
/// <c>GetStageAssignmentsExportQuery</c> already prints. That is deliberate: the file somebody
/// downloads to read and the file somebody uploads to write must be the same document, or the second
/// is a form nobody has ever seen.</para>
///
/// <para>⚠ <b>Both identifiers travel, and the appariement indexes both.</b> <c>Student.CNE</c> is
/// optional — 46 % of the roll carried none before 01/09/2026 — and <c>Appogee</c> is the one that is
/// in practice always present. A canvas keyed on the CNE alone would designate nobody for half the
/// faculty.</para>
///
/// <para>Values stay raw: an unparseable date arrives as null and is reported against its own line,
/// rather than failing the whole upload with nothing to show for it.</para>
/// </remarks>
public sealed record AffectationSheetRow(
    int SheetRow,
    string? Appogee,
    string? Cne,
    string? StageName,
    string? ServiceName,
    string? HospitalName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? DelocalizationReason);

/// <summary>What the import will do about one line — or why it can do nothing.</summary>
public enum AffectationSheetRowStatus
{
    /// <summary>The student holds no affectation for this stage; it is created, with this période.</summary>
    WillCreate,

    /// <summary>
    /// He holds one, and the sheet says something else: the existing périodes are dropped and rebuilt
    /// from the file.
    ///
    /// <para>⚠ <b>This is the destructive half of the act and the reason it needs a confirmed
    /// count.</b> What is dropped is stated separately — <c>AffectationSheetReport.PeriodsToDrop</c> —
    /// because « 40 affectations remplacées » says nothing about whether that destroyed 40 périodes
    /// or 200.</para>
    /// </summary>
    WillReplace,

    /// <summary>
    /// The stage is served outside the faculty: one ad-hoc période, created already started and
    /// complete, with the motif on record. Goes through <c>InternshipAssignment.Delocalize</c>, which
    /// replaces whatever was there — so this covers the create and the replace at once.
    /// </summary>
    WillDelocalize,

    /// <summary>
    /// The affectation already says exactly this — same services, same dates, same count. Skipped.
    ///
    /// <para>⚠ <b>This is what makes a corrected file safe to re-send</b>, and it is deliberately
    /// checked <i>before</i> <see cref="AlreadyMarked"/>: a promotion whose évaluations have started
    /// arriving must not become un-re-uploadable because the file still describes, correctly, what
    /// those marks were given for.</para>
    /// </summary>
    Unchanged,

    /// <summary>
    /// The line names a student and a stage and says nothing about where or when — the canvas as it
    /// came out, for a stage nobody has planned yet. Skipped and counted.
    ///
    /// <para>⚠ <b>A skip and not a refusal, and the distinction is what makes the canvas usable.</b>
    /// The document goes out with a line per (student, stage the level requires), so planning one
    /// stage of a promotion means uploading a file in which every other line is blank. Refusing those
    /// would mean deleting several hundred rows by hand before every upload, and a file that is
    /// painful to re-send stops being re-sent — which is how a correction never gets applied.</para>
    ///
    /// <para>⚠ <b>A <i>half</i>-filled line is still a refusal</b> — see <see cref="MissingDates"/>.
    /// Blank everywhere is « je n'y ai pas touché »; a service with no dates is somebody who meant to
    /// fill the line and stopped, and quietly skipping that is how a student goes unplanned with
    /// nothing on screen saying so.</para>
    /// </summary>
    NotPlanned,

    // ---- refusals: one of these anywhere refuses the whole file ----

    /// <summary>Neither identifier is filled, so the line designates nobody.</summary>
    NoIdentifier,

    /// <summary>The same (student, stage, service, dates) appears twice.</summary>
    DuplicateRow,

    /// <summary>No student carries this identifier. ⚠ Never a creation — see the class remarks.</summary>
    StudentNotFound,

    /// <summary>The student exists but holds no registration in the promotion the file was cut for.</summary>
    WrongPromotion,

    /// <summary>
    /// The registration carries a blocking signalement, so planning must leave it alone —
    /// <c>RegistrationHoldPolicy</c>. A spreadsheet is exactly the route by which a frozen student
    /// gets planned anyway.
    /// </summary>
    OnHold,

    /// <summary>
    /// He is in no roster, so there is no cohorte to attach the affectation to. The découpage comes
    /// first; this is the refusal that says so.
    /// </summary>
    NoRoster,

    /// <summary>No stage of this promotion's level carries that name.</summary>
    UnknownStage,

    /// <summary>Two stages of the level carry that name, so the line names neither.</summary>
    AmbiguousStage,

    /// <summary>No service carries that name.</summary>
    UnknownService,

    /// <summary>Several services carry that name and the « Hôpital » cell does not separate them.</summary>
    AmbiguousService,

    /// <summary>A date cell is empty or unreadable.</summary>
    MissingDates,

    /// <summary>The end is before the start.</summary>
    BadDateOrder,

    /// <summary>
    /// The affectation carries a mark and the sheet describes something different.
    ///
    /// <para>⚠ <b>The one thing this act may never destroy.</b> Same bargain
    /// <c>InternshipAssignment.Delocalize</c> already makes: a mark is the single thing here that
    /// nothing puts back, and no bulk act may be able to erase one. An identical file is
    /// <see cref="Unchanged"/> and passes.</para>
    /// </summary>
    AlreadyMarked,

    /// <summary>
    /// The affectation's périodes carry attendance, and the sheet describes something different.
    ///
    /// <para>⚠ <b>The second thing this act may never destroy, and it was missing until
    /// 13/09/2026.</b> <c>AttendanceRecord</c> cascades from <c>ServicePeriod</c>, so rebuilding a
    /// rotation that has begun deleted the days a secretary keyed in one by one — silently, because a
    /// mark announces itself on every screen and attendance is invisible until the day it is needed.
    /// Same bargain as <see cref="AlreadyMarked"/>: this act rewrites a plan, never a record of what
    /// happened.</para>
    ///
    /// <para>⚠ <b>It is also what makes the undo total.</b> Since an import destroys nothing carrying
    /// attendance or a mark, everything it does destroy is a service, a window and a few flags — which
    /// is exactly what <c>ReplacedPeriod</c> records, so « annuler l'import » puts back all of it and
    /// not merely most of it.</para>
    /// </summary>
    AlreadyAttended,

    /// <summary>
    /// The line names an external service, or fills the motif, and the other half is missing.
    /// <c>Delocalization.Reason</c> is the only trace the faculty holds of a stage nobody here
    /// supervised, so it is required rather than defaulted.
    /// </summary>
    DelocalizationWithoutReason,

    /// <summary>
    /// One affectation mixes a délocalisation with in-faculty périodes, or spreads a délocalisation
    /// over several lines. A délocalisé stage is served in one place, outside; the aggregate models it
    /// as exactly one période and a second line would silently be dropped by the first.
    /// </summary>
    MalformedDelocalization,

    /// <summary>
    /// The student holds <b>several</b> affectations for this stage in this registration, so the line
    /// does not say which one it describes.
    ///
    /// <para>⚠ A retake is a second <c>InternshipAssignment</c> on the same stage — that is how a
    /// failed stage is re-opened — so this is a real state and not a corruption. It is refused rather
    /// than resolved by a rule of thumb: « the most recent one » would silently rewrite a rattrapage
    /// on the strength of a row ordering nobody chose.</para>
    /// </summary>
    AmbiguousAffectation,
}

public static class AffectationSheetRowStatusExtensions
{
    /// <summary>
    /// A line that cannot be applied. ⚠ <b>One of these anywhere refuses the whole file</b>, unlike
    /// the mass délocalisation, which writes what it can and names the rest.
    /// </summary>
    /// <remarks>
    /// The two acts differ in what a partial result leaves behind. A délocalisation skipped for one
    /// student leaves that student where he already was — a state somebody authored. This act
    /// <i>builds</i> a promotion's execution records, so applying 800 lines and refusing 12 leaves a
    /// year half-planned, and a half-planned year reads exactly like a plan somebody meant to author.
    /// It is also PGSH's own canvas: a line it cannot resolve is a line somebody edited into a
    /// mistake, not a line the faculty wrote differently.
    /// </remarks>
    public static bool IsError(this AffectationSheetRowStatus status) =>
        status is not (AffectationSheetRowStatus.WillCreate
                    or AffectationSheetRowStatus.WillReplace
                    or AffectationSheetRowStatus.WillDelocalize
                    or AffectationSheetRowStatus.Unchanged
                    or AffectationSheetRowStatus.NotPlanned);

    /// <summary>
    /// Lines a human has to look at, though only the errors block. Ordered first in the report, so the
    /// cap on the row list can never hide one.
    /// </summary>
    public static bool NeedsAttention(this AffectationSheetRowStatus status) =>
        status.IsError() || status is AffectationSheetRowStatus.WillReplace;
}

/// <summary>What one line does, and to whom.</summary>
/// <param name="OutsideCnpn">
/// The stage is not in the requirement set the student's own text gives his level.
///
/// <para>⚠ <b>Reported, never refused.</b> The automatic découpage refuses it — a plan that quietly
/// omits a partition is worse than one that says why — but this sheet <i>is</i> the human override,
/// and arrêté 1650.25's requirements have not all been entered: an enforcing check here would refuse
/// files on the strength of data nobody has typed in. It is carried per row rather than as a bare
/// count, because a number with no names sends nobody anywhere.</para>
/// </param>
public sealed record AffectationSheetRowReport(
    int SheetRow,
    string? Identifier,
    string? StudentFullName,
    string? StageName,
    string? ServiceName,
    AffectationSheetRowStatus Status,
    bool OutsideCnpn,
    string Message);

/// <summary>How one stage fares in the file. One entry per stage, so it is bounded however long the file is.</summary>
public sealed record AffectationSheetStageBreakdown(
    string StageName,
    int Rows,
    int Affectations,
    int WillCreate,
    int WillReplace,
    int WillDelocalize,
    int Unchanged,
    int Errors);

/// <summary>
/// The dry run, and — after an apply — the record of what was written. The same shape both times,
/// because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
/// <param name="Affectations">
/// Distinct (student, stage) units the file describes. This, not <paramref name="TotalRows"/>, is what
/// the apply confirms — see <c>ApplyAffectationSheetCommand.ConfirmedCount</c>.
/// </param>
/// <param name="PeriodsToDrop">
/// Existing périodes the replacements destroy. ⚠ Stated on its own because
/// <c>WillReplace</c> alone cannot say how much: forty affectations replaced is forty périodes
/// destroyed or two hundred, and the operator is authorising the second number, not the first.
/// </param>
/// <param name="NotPlanned">
/// Lines left blank — the canvas's own « pas encore planifié ». ⚠ Reported rather than silent: this
/// number and <paramref name="Affectations"/> together are the only way to read « j'ai rempli trois
/// stages sur onze » off the file, and a blank that says nothing is this codebase's recurring defect.
/// </param>
/// <param name="PublishedPeriodsToDrop">
/// How many of <paramref name="PeriodsToDrop"/> came from the planning grid.
///
/// <para>⚠ Stated apart because it is a different loss. An ad-hoc période destroyed is a line
/// somebody typed and can type again; a published one is a rotation the grid produced, and what
/// replaces it is <b>hors grille</b> — the cells stay where they are, the promotion's plan and its
/// execution records stop agreeing, and re-publishing will not put it back, because publication skips
/// an affectation that already carries a période. Zero here is the ordinary case, and a number is the
/// operator being told he is about to overwrite a répartition rather than fill an empty one.</para>
/// </param>
/// <param name="CohortsToCreate">
/// Group×stage pairs the file needs and PGSH does not hold. Counted separately for the same reason the
/// découpage canvas counts « groupes à créer » separately: a spreadsheet giving birth to rows nobody
/// authored is worth its own line on the confirmation.
/// </param>
/// <param name="NotCovered">
/// Registrations of the promotion no line of the file mentions.
///
/// <para>⚠ <b>Silence here means nothing at all</b> — and that is exactly why it is reported. Unlike
/// the réinscription roll, where an absence is a statement, a partial canvas is the ordinary way to
/// use this one: fixing a single stage means uploading the rows of that stage. So absentees are never
/// touched, and the count exists only so « 640 inscriptions non nommées » can be read as « j'ai
/// téléversé une seule promotion sur trois » instead of being discovered in March.</para>
/// </param>
/// <param name="Notes">
/// What the numbers do <b>not</b> say, in words. ⚠ The load-bearing one is that these périodes are
/// hors grille: the planning grid reads <c>CohortSlotAssignment</c>s, so an imported rotation is
/// visible on the service's own page and on the student's dossier, and invisible in the grid and in
/// the occupancy figures the grid prints. A number standing for two states is this codebase's
/// recurring defect; here the sheet is the second state.
/// </param>
public sealed record AffectationSheetReport(
    string YearLabel,
    string LevelLabel,
    int TotalRows,
    int Affectations,
    int WillCreate,
    int WillReplace,
    int WillDelocalize,
    int Unchanged,
    int NotPlanned,
    int PeriodsToWrite,
    int PeriodsToDrop,
    int PublishedPeriodsToDrop,
    int CohortsToCreate,
    int StudentsCovered,
    int NotCovered,
    int OutsideCnpn,
    int ErrorCount,
    bool CanApply,
    IReadOnlyList<AffectationSheetStageBreakdown> ByStage,
    IReadOnlyList<AffectationSheetRowReport> Rows,
    bool RowsTruncated,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads an uploaded canevas des affectations. The port lives here so the application layer never
/// learns what .xlsx is; the ClosedXML adapter sits in Infrastructure beside the other four.
/// </summary>
/// <remarks>
/// There is no <c>BuildTemplate</c> on this one. The canvas is built through the shared
/// <c>ExportWorkbook</c> model and written by <c>IExportWorkbookWriter</c> — the same machinery that
/// prints the stage export it mirrors, so the downloaded document and the uploaded one cannot drift
/// into looking like two different faculties.
/// </remarks>
public interface IAffectationSheetParser
{
    /// <summary>Reads every data row. Cell-level mistakes come through as null for the planner to
    /// report against their own line, never as an exception.</summary>
    IReadOnlyList<AffectationSheetRow> Parse(Stream sheet);
}
