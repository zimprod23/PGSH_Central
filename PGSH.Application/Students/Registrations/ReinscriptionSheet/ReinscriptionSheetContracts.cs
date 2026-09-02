using PGSH.Domain.Registrations;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// One line of the faculty's réinscription roll, exactly as it was typed.
/// </summary>
/// <remarks>
/// <para>The real file — « Réinscriptions 26-27 VF.xlsx », 6 862 rows — carries five columns:
/// <c>Code</c>, <c>NOM</c>, <c>PRENOM</c>, <c>Etape 25-26</c>, <c>Etape 2026/2027</c>. It is not the
/// canvas PGSH generates for the déliberation, and it is not a list of exceptions: it is the roll
/// itself, one line per student who re-registers, stating where he was and where he goes.</para>
///
/// <para>⚠ <b><see cref="Code"/> is the numéro Apogée</b> — the legacy <c>NO_ORDRE</c>, which the
/// import carries across verbatim. Measured against the source base: 6 813 of the 6 862 codes match
/// a student exactly, none is duplicated, and every one of the 6 810 rows whose student holds a
/// 2025-2026 registration agrees with it about the level. The CNE is not in the file at all, which
/// is one more reason <c>Student.CNE</c> may legitimately be absent.</para>
///
/// <para>Values stay raw — untrimmed, unparsed — so a mistyped cell is reported against its own row
/// rather than failing the whole upload with nothing to show for it.</para>
/// </remarks>
public sealed record ReinscriptionSheetRow(
    int SheetRow,
    string? Code,
    string? LastName,
    string? FirstName,
    string? FromLevelCode,
    string? ToLevelCode);

/// <summary>What the roll will do about one line — or why it can do nothing.</summary>
public enum ReinscriptionSheetRowStatus
{
    /// <summary>The registration for the target year will be created, and the closing year's verdict
    /// recorded from the level movement the faculty stated. Nothing to look at.</summary>
    WillRegister,

    /// <summary>
    /// The registration will be created, but the student holds none in the closing year, so there is
    /// nothing to pronounce on. Reported rather than refused: the file's statement about where he
    /// goes is usable even where its statement about where he was cannot be checked. Three rows of
    /// the 2026-2027 file are this — students returning after an interrupted year.
    /// </summary>
    WillRegisterWithoutSource,

    /// <summary>Already registered for the target year — the roll is re-runnable, so this is a skip.</summary>
    AlreadyRegistered,

    /// <summary>A programme PGSH deliberately does not manage (the masters). Skipped and counted.</summary>
    OutsideScope,

    /// <summary>
    /// No student carries this code, so the roll <b>creates</b> him — and flags the thin dossier.
    /// </summary>
    /// <para>⚠ <b>This used to be a skip, on the rule that creating an identity is the inscription's
    /// act and not the rollover's.</b> The rule is sound and the skip was still wrong in practice: the
    /// 26 such rows of the 2026-2027 file ended up in a downloaded spreadsheet and nowhere anybody
    /// works, so nobody acted on them. They are created from what the file carries — the Apogée and
    /// the name — and marked <c>IncompleteStudentFile</c>.</para>
    ///
    /// <para>⚠ <b>That flag does not freeze him.</b> His dossier is thin, not wrong: he is cut into a
    /// roster and planned like anyone else while somebody fills in the CNE, the real e-mail and the
    /// date de naissance. Freezing him would be treating a missing birth date like an unexplained
    /// absence.</para>
    ///
    /// <para>⚠ <b>Only the e-mail is invented</b>, because <c>Users.Email</c> is NOT NULL UNIQUE. No
    /// CNE is manufactured — the row has an Apogée, and <c>Student.CNE</c> is optional.</para>
    WillCreateStudent,

    /// <summary>
    /// The registration <b>is</b> created, and immediately held: the target level is the last year of
    /// his own cursus and an earlier stage reads as unvalidated.
    ///
    /// <para>⚠ <b>This used to be a skip, and the skip was the defect.</b> The final year is not a
    /// year one passes — there is no déliberation for it. The student sits in it, revalidates his
    /// stages one at a time, and is re-registered each September until they are done, so the
    /// re-registration <em>is</em> the mechanism that clears the debt. Measured on the 2026-2027
    /// roll, refusing it dropped 182 of the 651 7ᵉ année Médecine the faculty itself named as coming
    /// back — and in most of those cases the stage was served and the évaluation simply is not keyed
    /// in yet, which is a fact about our data entry and not about the student.</para>
    ///
    /// <para>So the faculty's document wins and PGSH records its own disagreement instead of acting
    /// on it: the registration exists, <c>RegistrationHoldReason.OutstandingPriorStages</c> keeps it
    /// out of every roster and every affectation, and scolarité releases it once the évaluations are
    /// in — or has him revalidate. He may not start his final year's stages before the earlier ones
    /// are settled, which is exactly what the hold expresses and what a refusal could not.</para>
    /// </summary>
    WillRegisterHeld,

    // ---- refusals: one of these anywhere refuses the whole file ----

    /// <summary>The <c>Code</c> cell is empty, so the line designates nobody.</summary>
    NoIdentifier,

    /// <summary>The same code appears on two lines.</summary>
    DuplicateRow,

    /// <summary>A level code PGSH has never been told about — neither a promotion nor a known
    /// out-of-scope programme. Somebody has to say which of the two it is.</summary>
    UnknownLevelCode,

    /// <summary>The level the file says he was in is not the one his registration records.</summary>
    LevelMismatch,

    /// <summary>« Retrait » is a status wearing a level's clothes; nobody is re-registered into one.</summary>
    NotAPromotion,

    /// <summary>The target level is <em>below</em> the one he was in, which no verdict produces.</summary>
    LevelRegression,

    /// <summary>PGSH holds no level for a code that names one — the catalogue is incomplete.</summary>
    LevelMissing,
}

public static class ReinscriptionSheetRowStatusExtensions
{
    /// <summary>
    /// A line that cannot be applied. One of these anywhere refuses the whole file.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The line between a refusal and a skip is whether the row is <em>wrong</em> or merely
    /// <em>not actionable</em>.</b> A code that matches nobody, a master's programme, a student
    /// already rolled over — none of those says the file is mistaken, and refusing 6 800 rows over
    /// them would buy nothing, because the apply is idempotent and can simply be re-run once the
    /// student is inscribed. A level disagreement or a duplicated code <em>is</em> a mistake, and it
    /// is the kind that writes a verdict onto the wrong student's year, which nothing puts back.
    /// </remarks>
    public static bool IsError(this ReinscriptionSheetRowStatus status) =>
        status is ReinscriptionSheetRowStatus.NoIdentifier
               or ReinscriptionSheetRowStatus.DuplicateRow
               or ReinscriptionSheetRowStatus.UnknownLevelCode
               or ReinscriptionSheetRowStatus.LevelMismatch
               or ReinscriptionSheetRowStatus.NotAPromotion
               or ReinscriptionSheetRowStatus.LevelRegression
               or ReinscriptionSheetRowStatus.LevelMissing;

    /// <summary>
    /// Rows a human has to look at before the rollover is complete, though only the errors block it.
    /// Ordered first in the report, so the cap on the row list can never hide one.
    /// </summary>
    public static bool NeedsAttention(this ReinscriptionSheetRowStatus status) =>
        status.IsError()
        || status is ReinscriptionSheetRowStatus.WillCreateStudent
                  or ReinscriptionSheetRowStatus.WillRegisterHeld
                  or ReinscriptionSheetRowStatus.WillRegisterWithoutSource;
}

/// <summary>What one line of the roll does, and to whom.</summary>
/// <param name="Outcome">
/// The verdict this line writes onto the closing year's registration, if any. Null on a final-year
/// repeat, which is the thesis year running its course and not a failure — see <c>FinalYearTest</c>.
/// </param>
public sealed record ReinscriptionSheetRowReport(
    int SheetRow,
    string? Code,
    string? StudentFullName,
    string? FromLevelLabel,
    string? ToLevelLabel,
    ReinscriptionSheetRowStatus Status,
    RegistrationStatus? Outcome,
    string Message);

/// <summary>
/// What an absence from the roll means for one closing-year registration.
/// </summary>
/// <remarks>
/// ⚠ <b>Every one of these is held</b>, <see cref="Graduating"/> included — see
/// <c>ReinscriptionSheetReport.AbsenteesHeld</c>. The enum says what PGSH could conclude, not
/// whether the row needs a human: all of them do, which is why the hold does not branch on it.
/// </remarks>
public enum ReinscriptionSheetAbsenceOutcome
{
    /// <summary>
    /// Absent in the last year of his own text: he has defended. Recorded « Diplômé »,
    /// <c>Inferred</c> — so a real defence roll arriving later, which is <c>Declared</c>, corrects it
    /// by itself. The one thing an absence decides.
    /// </summary>
    Graduating,

    /// <summary>
    /// Absent, and the year already carries a verdict. It is left exactly as it stands — an
    /// <c>Inferred</c> reading may not overwrite a <c>Declared</c> one, and re-deriving an inferred
    /// verdict says nothing new — but the absence itself is still unexplained, so the hold stands.
    /// </summary>
    AlreadyDecided,

    /// <summary>
    /// Absent below the last year of his own text. An absence is only decidable at the end of a
    /// cursus; here it could be an abandon, an exclusion, or a réinscription that has not arrived.
    /// </summary>
    BelowFinalYear,

    /// <summary>
    /// Absent, and PGSH holds no CNPN for him — so there is no number saying whether that was his
    /// last year. ⚠ Deliberately not resolved from the programme's shortest text: that fallback is
    /// right for standing aside and wrong for ending a cursus.
    /// </summary>
    NoTextOnRecord,

    /// <summary>« Retrait » and its kind. No cursus to end.</summary>
    NotAPromotion,
}

/// <summary>
/// One registration of the closing year that the file does not mention and that could not be decided
/// from that absence. Named, because these are the rows somebody has to act on.
/// </summary>
public sealed record ReinscriptionSheetAbsentee(
    Guid StudentId,
    string StudentFullName,
    string? Appogee,
    string LevelLabel,
    ReinscriptionSheetAbsenceOutcome Outcome,
    string Message);

/// <summary>
/// How one promotion of the closing year fares in the roll. One entry per level, so it is bounded
/// however long the file is.
/// </summary>
public sealed record ReinscriptionSheetLevelBreakdown(
    string FromLevelLabel,
    int Listed,
    int WillRegister,
    int NeedsAttention);

/// <summary>
/// The dry run, and — after an apply — the record of what was written. The same shape both times,
/// because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
/// <param name="WillRecordOutcome">
/// Verdicts written onto the closing year's registrations. Always fewer than <c>WillRegister</c>:
/// a final-year repeat is registered without one.
/// </param>
/// <param name="NotCovered">
/// Registrations of the closing year that no line of the file mentions — the total, of which
/// <paramref name="WillGraduate"/>, <paramref name="AbsentNeedingAttention"/> and
/// <paramref name="AbsentAlreadyDecided"/> are the parts.
///
/// <para>⚠ <b>Silence here is the roll's, not the déliberation's.</b> That canvas is a list of
/// exceptions, so everyone unnamed is admis; this one is the list of who <em>is</em> coming back, so
/// everyone unnamed is not — a graduate, an exclusion, an abandon.</para>
/// </param>
/// <param name="WillGraduate">
/// Absentees in the <b>last year of their own text</b>, recorded « Diplômé ».
///
/// <para>⚠ <b>This is the one thing an absence decides, and it is why the apply needs a confirmed
/// count.</b> Every other write this act performs lands on a student the file names; these land on
/// students it does not, so a registration created between the preview and the apply would be
/// graduated by a confirmation nobody gave for it — exactly the case
/// <c>ApplyDeliberationCommand.ConfirmedDefaultCount</c> exists for.</para>
///
/// <para>Recorded <c>Inferred</c>, never <c>Declared</c>: nobody named these students on a document.
/// That also makes the correction free — a real defence roll is <c>Declared</c>, and <c>Declared</c>
/// overwrites <c>Inferred</c> while the reverse is refused. Measured on the 2026-2027 roll: 1 006 in
/// 7ᵉ année Médecine and 212 in 6ᵉ année Pharmacie.</para>
/// </param>
/// <param name="AbsentNeedingAttention">
/// Absentees an absence cannot decide — below a final year, or with no text on record. 47 on the
/// 2026-2027 roll. Left untouched and named in <paramref name="Absentees"/>.
/// </param>
/// <param name="AbsentAlreadyDecided">
/// Absentees already carrying a verdict. The verdict is never touched — <c>Inferred</c> may not
/// overwrite <c>Declared</c>, and re-deriving an inferred one says nothing new — but the
/// registration is still held, like every other absentee.
/// </param>
/// <param name="CreatedStudents">
/// Students the roll creates because it names them and PGSH does not hold them. Each is registered at
/// the level the file states, and flagged « dossier à compléter » — an <b>advisory</b> signalement, so
/// they partition and plan with everyone else.
/// </param>
/// <param name="GeneratedEmails">
/// How many addresses are manufactured for those students. ⚠ Never silent: an e-mail is a login, and
/// one that collides with a real address would hand a student another person's account, so the count
/// is stated and each address is shown on its own row of the report.
/// </param>
/// <param name="AbsenteesHeld">
/// Closing-year registrations withdrawn from planning because the roll does not name them. Equal to
/// <paramref name="NotCovered"/>: <b>every</b> absentee is held, the graduations included.
///
/// <para>⚠ <b>Why the 1 217 graduations are held too, when their cursus is over anyway.</b> Because
/// the graduation is <em>our inference</em>, not the faculty's statement — it is read off a blank
/// cell. If the roll was partial, « il a soutenu » is wrong for people who are simply still enrolled,
/// and nothing on the row would say a human had ever looked. The hold is what stops the inference
/// being acted on before somebody confirms it, and it costs those students nothing they were going
/// to use: their closing year is closed. It also catches the case an absence most often really is —
/// a réinscription that has not arrived yet — because the hold is then still standing on the day
/// somebody registers him by hand.</para>
///
/// <para>Holds need no confirmed count of their own, unlike <paramref name="WillGraduate"/>: a hold
/// is released in one click and the row keeps its history, while a graduation ends a cursus and
/// nothing puts that back. Confirm what cannot be undone.</para>
/// </param>
public sealed record ReinscriptionSheetReport(
    string FromYearLabel,
    string ToYearLabel,
    int TotalRows,
    int WillRegister,
    int WillRecordOutcome,
    int AlreadyRegistered,
    int OutsideScope,
    int CreatedStudents,
    int WithoutSourceRegistration,
    int WillRegisterHeld,
    int ErrorCount,
    int NotCovered,
    int WillGraduate,
    int AbsentNeedingAttention,
    int AbsentAlreadyDecided,
    int AbsenteesHeld,
    int GeneratedEmails,
    bool CanApply,
    IReadOnlyDictionary<string, int> ByTargetLevel,
    IReadOnlyList<ReinscriptionSheetLevelBreakdown> ByLevel,
    IReadOnlyList<ReinscriptionSheetRowReport> Rows,
    bool RowsTruncated,
    IReadOnlyList<ReinscriptionSheetAbsentee> Absentees,
    bool AbsenteesTruncated);

/// <summary>
/// Reads an uploaded réinscription roll. The port lives here so the application layer never learns
/// what .xlsx is; the ClosedXML adapter sits in Infrastructure, beside the other three.
/// </summary>
/// <remarks>
/// There is deliberately <b>no</b> <c>BuildTemplate</c>. The other three canvases are documents PGSH
/// hands out and gets back; this one is a document the faculty already produces for its own purposes,
/// and generating a rival version of it would only invite the two to drift. The parser therefore
/// accommodates the file rather than dictating it: headers are matched loosely, and the two level
/// columns are found by their « Etape » prefix and their order rather than by a year suffix that
/// changes annually.
/// </remarks>
public interface IReinscriptionSheetParser
{
    /// <summary>Reads every data row. Cell-level mistakes are carried through as null or raw values
    /// for the planner to report, not thrown.</summary>
    IReadOnlyList<ReinscriptionSheetRow> Parse(Stream sheet);
}
