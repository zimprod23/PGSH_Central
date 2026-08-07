using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Evaluations.Import;

/// <summary>
/// How far one sheet row reaches. Chosen explicitly at upload and never inferred from which columns
/// happen to be filled: an import tool that guesses is an import tool nobody can predict.
/// </summary>
public enum EvaluationImportScope
{
    /// <summary>One verdict per student, applied to every (non-interrupted) rotation of the stage.</summary>
    WholeStage,

    /// <summary>One verdict per student, applied to a single numbered rotation of the stage.</summary>
    SinglePeriod,
}

/// <summary>
/// One line of the uploaded sheet, exactly as it was typed. Values stay raw — the verdict is carried
/// as text and the identifiers untrimmed — so that a mistyped cell is reported back against its own
/// row instead of failing the whole parse.
/// </summary>
public sealed record EvaluationImportRow(
    int SheetRow,
    string? Cne,
    string? Appogee,
    int? PeriodNumber,
    string? Verdict,
    decimal? Mark,
    string? Remark);

/// <summary>What will happen to a row — or why nothing can.</summary>
public enum EvaluationImportRowStatus
{
    /// <summary>No mark on record yet; the import records one.</summary>
    WillCreate,

    /// <summary>A mark is already on record; the import replaces it. Amending is a requirement, not an error.</summary>
    WillOverwrite,

    NoIdentifier,
    UnknownStudent,
    NotInStage,
    DuplicateStudent,
    PeriodNotFound,
    PeriodNotClosed,
    AlreadyRatified,
    NotAllowed,
    MissingValue,
    InvalidValue,
}

public static class EvaluationImportRowStatusExtensions
{
    /// <summary>A row that cannot be applied. One of these anywhere refuses the whole import.</summary>
    public static bool IsError(this EvaluationImportRowStatus status) =>
        status is not (EvaluationImportRowStatus.WillCreate or EvaluationImportRowStatus.WillOverwrite);
}

public sealed record EvaluationImportRowReport(
    int SheetRow,
    string? Cne,
    string? Appogee,
    string? StudentFullName,
    EvaluationImportRowStatus Status,
    string Message,
    // How many rotations this row writes to — more than one in whole-stage scope.
    int PeriodCount);

/// <summary>
/// The dry run, and — after a successful apply — the record of what was written. Same shape both
/// times because it is the same plan: the preview the user confirmed is literally what runs.
/// </summary>
public sealed record EvaluationImportReport(
    int TotalRows,
    int WillCreate,
    int WillOverwrite,
    int ErrorCount,
    // Rotations that will be (or were) written — a whole-stage row counts once per rotation.
    int PeriodCount,
    bool CanApply,
    IReadOnlyList<EvaluationImportRowReport> Rows);

/// <summary>
/// A stage's own students and rotations, used to generate a pre-filled sheet. Nobody should have to
/// hand-build the columns or retype an identifier — a mistyped CNE is a row that silently belongs to
/// no one.
/// </summary>
public sealed record EvaluationImportTemplate(
    string StageName,
    EvaluationImportScope Scope,
    EvaluationMode Mode,
    int? PeriodNumber,
    IReadOnlyList<EvaluationImportTemplateStudent> Students);

public sealed record EvaluationImportTemplateStudent(string Cne, string Appogee, string FullName, string GroupLabel);

/// <summary>
/// Reads an uploaded evaluation sheet and writes the blank one. The port lives here so the
/// application layer never learns what .xlsx is; the ClosedXML adapter sits in Infrastructure.
/// </summary>
public interface IEvaluationSheetParser
{
    /// <summary>Reads every data row of <paramref name="sheet"/>. Cell-level mistakes are carried
    /// through as null/raw values for the planner to report, not thrown.</summary>
    IReadOnlyList<EvaluationImportRow> Parse(Stream sheet);

    /// <summary>Builds the pre-filled workbook a user downloads before entering marks.</summary>
    byte[] BuildTemplate(EvaluationImportTemplate template);
}
