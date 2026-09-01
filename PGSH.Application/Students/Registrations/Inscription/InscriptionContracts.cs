namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// What one inscription file covers: <b>one promotion of one academic year</b>.
/// </summary>
/// <remarks>
/// <para><b><see cref="LevelId"/> is required, and it is the guard rather than a filter.</b> The
/// déliberation may leave it out because the students it names already hold a registration, and the
/// year is what makes an identifier unambiguous. Nobody on an inscription sheet holds one — that is
/// the definition of the act — so the level cannot be discovered from the student and has to be
/// stated. A sheet that could carry rows for several promotions would also have to name each row's
/// level in free text, and a mistyped level is a student enrolled in the wrong year of study with
/// nothing to catch it.</para>
///
/// <para>An omitted <see cref="AcademicYearId"/> resolves to the current year, never to all of them.</para>
/// </remarks>
public sealed record InscriptionScope(int LevelId, int? AcademicYearId);

/// <summary>
/// One line of an uploaded inscription sheet, exactly as it was typed. Every value stays raw — no
/// trimming, no parsing, no enum conversion — so a mistyped cell is reported against its own row
/// instead of failing the whole parse. Same contract as <c>DeliberationRow</c>.
/// </summary>
public sealed record InscriptionRow(
    int SheetRow,
    string? Cne,
    string? Appogee,
    string? LastName,
    string? FirstName,
    string? Cin,
    string? Email,
    string? Gender,
    string? DateOfBirth,
    string? PlaceOfBirth,
    string? BacYear,
    string? BacSeries,
    string? AccessGrade,
    string? Agreement,
    // Provenance, for a student arriving from outside.
    string? OriginInstitution,
    string? OriginCountry,
    string? OriginLastYearCompleted,
    string? EquivalenceReference,
    string? EquivalenceDate);

/// <summary>
/// What one row will do — or why nothing can.
/// </summary>
/// <remarks>
/// <para>The four writing actions <b>partition</b> the rows this act exists for, on two independent
/// questions: does PGSH already hold this person, and is he entering the programme he was already in.
/// Nothing else is a kind:</para>
/// <list type="bullet">
/// <item><b>« Sous convention » is not one of them.</b> <c>Student.AgreementType</c> says how a
/// student's place is funded — payée amie, international — and an étudiant sous convention can be any
/// of the four: a first-year arriving under an agreement is a <see cref="NewEntrant"/>, one arriving
/// in 3ᵉ année is a <see cref="TransferIn"/>. Made a fifth kind it would overlap the others and the
/// counts would stop adding up. It is a column any row may carry.</item>
/// <item><b>Neither is « redoublant ».</b> A student repeating is carried over by the réinscription
/// from his own verdict; he holds last year's registration and this act never sees him.</item>
/// </list>
/// </remarks>
public enum InscriptionAction
{
    /// <summary>Unknown to PGSH, entering the first year of a cursus. The September intake.</summary>
    NewEntrant,

    /// <summary>
    /// Unknown to PGSH, entering above the first year — he did the years below somewhere else. The
    /// provenance columns are required, and become a <c>PriorEnrolment</c>.
    /// </summary>
    TransferIn,

    /// <summary>
    /// Known to PGSH, holds no registration this year, and stays in his programme. The student who
    /// withdrew and came back — two of the twelve « Retrait » students did exactly this — or one the
    /// réinscription could not carry because the year he left was never closed.
    /// </summary>
    Returning,

    /// <summary>
    /// Known to PGSH, and the level named belongs to another programme: a réorientation. His
    /// <c>AcademicProgram</c> moves with him, and so does his CNPN stamp — the text he was on governs
    /// a cursus he has left.
    /// </summary>
    ProgrammeChange,

    /// <summary>
    /// Already registered in the target year. <b>Not an error.</b> This act creates people, so it has
    /// to be re-runnable: scolarité appends the ten late arrivals to the file it already sent and
    /// re-uploads it. Same choice as the réinscription's <c>AlreadyRegistered</c>, and the opposite
    /// of the déliberation, whose file is not stored and therefore cannot be re-sent.
    /// </summary>
    AlreadyRegistered,

    // Refusals.

    /// <summary>Neither CNE nor numéro Apogée: the line designates nobody.</summary>
    NoIdentifier,

    /// <summary>A student record cannot be created without a name.</summary>
    MissingName,

    /// <summary>The same person appears twice in the file.</summary>
    DuplicateInFile,

    /// <summary>
    /// An identifier on this row already belongs to somebody else — the CNE is one student's and the
    /// Apogée another's, or a CIN or an e-mail is already taken. All four are unique in the store, so
    /// guessing which cell is the mistyped one would either create the wrong person or fail at
    /// SaveChanges with nothing actionable in the message.
    /// </summary>
    IdentifierConflict,

    /// <summary>A new student above the first year, with nothing saying where he studied before.</summary>
    OriginRequired,

    /// <summary>A cell PGSH could not read — a date, a decimal, a série de bac.</summary>
    InvalidValue,

    /// <summary>
    /// The final year cannot be entered while an earlier stage is unvalidated. Reachable only for a
    /// student PGSH already holds: a newcomer has no cursus here to owe anything from.
    /// </summary>
    FinalYearBlocked,

    /// <summary>No address left on the domain for a generated e-mail. Supply one in the file.</summary>
    EmailUnavailable,
}

public static class InscriptionActionExtensions
{
    /// <summary>A row that cannot be applied. One of these anywhere refuses the whole file.</summary>
    public static bool IsError(this InscriptionAction action) =>
        action is not (InscriptionAction.NewEntrant
                    or InscriptionAction.TransferIn
                    or InscriptionAction.Returning
                    or InscriptionAction.ProgrammeChange
                    or InscriptionAction.AlreadyRegistered);

    /// <summary>A row that writes something.</summary>
    public static bool Writes(this InscriptionAction action) =>
        action is InscriptionAction.NewEntrant
               or InscriptionAction.TransferIn
               or InscriptionAction.Returning
               or InscriptionAction.ProgrammeChange;
}

/// <summary>What one row will do, and what is worth seeing about it before it does.</summary>
/// <param name="CreatesStudent">
/// A person is created, not merely a registration — the irreversible half of this act.
/// </param>
/// <param name="GeneratedEmail">
/// The address PGSH manufactured because the file carried none — <c>prenom_nom@um5.ac.ma</c>, the
/// same rule the legacy import used for all 10 204 imported students. Surfaced per row and counted in
/// the report, because an e-mail is a login: <c>SyncUserMiddleware</c> falls back to matching on it.
/// </param>
public sealed record InscriptionRowReport(
    int SheetRow,
    string? Cne,
    string? Appogee,
    string StudentFullName,
    InscriptionAction Action,
    bool CreatesStudent,
    string? GeneratedEmail,
    bool RecordsOrigin,
    string Message);

/// <summary>
/// The dry run, and — after a successful apply — the record of what was written. Same shape both
/// times because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
/// <param name="WillCreateStudents">Rows that create a person. The number the confirmation names.</param>
/// <param name="AlreadyRegistered">
/// Skipped rather than refused, so the file can be re-sent with names appended.
/// </param>
/// <param name="GeneratedEmails">How many addresses PGSH is about to manufacture. Never silent.</param>
public sealed record InscriptionReport(
    string AcademicYearLabel,
    string LevelLabel,
    int TotalRows,
    int WillCreateStudents,
    int WillRegister,
    int NewEntrants,
    int TransfersIn,
    int Returning,
    int ProgrammeChanges,
    int AlreadyRegistered,
    int ErrorCount,
    int GeneratedEmails,
    int OriginsRecorded,
    bool CanApply,
    IReadOnlyDictionary<string, int> ByAction,
    IReadOnlyList<InscriptionRowReport> Rows,
    // Rows is capped — an intake file is a whole promotion — and ordered so refusals come first.
    // The counts above stay exact.
    bool RowsTruncated);

/// <summary>The blank sheet scolarité fills in, described for the adapter that writes it.</summary>
/// <param name="OriginRequired">True above the first year: the provenance columns stop being optional.</param>
public sealed record InscriptionTemplate(
    string AcademicYearLabel,
    string LevelLabel,
    int LevelYear,
    bool OriginRequired);

/// <summary>
/// Reads an uploaded inscription sheet and writes the blank one. The port lives here so the
/// application layer never learns what .xlsx is; the ClosedXML adapter sits in Infrastructure.
/// </summary>
public interface IInscriptionSheetParser
{
    /// <summary>Reads every data row. Cell-level mistakes are carried through as raw values for the
    /// planner to report, not thrown.</summary>
    IReadOnlyList<InscriptionRow> Parse(Stream sheet);

    /// <summary>Builds the workbook scolarité downloads before the intake.</summary>
    byte[] BuildTemplate(InscriptionTemplate template);
}
