using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Exports;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// The réinscription report as a document: three sheets, and <b>nothing capped</b>.
/// </summary>
/// <remarks>
/// <para><b>Why the report needed an export at all.</b> What comes back from an upload is a screen —
/// bounded by <c>ReinscriptionSheetPlanner.MaxReportedRows</c>, ordered attention-first, with the
/// absentees truncated. That is right for a browser and useless as a working document: the 2026-2027
/// roll produces ~1 450 rows somebody has to walk through one at a time, against a cap of 1 000, so
/// the one list the operator actually needs is the one the screen cannot finish showing.</para>
///
/// <para>⚠ <b>It re-runs the planner rather than reading a stored report.</b> Nothing is stored — the
/// uploaded file is not kept — so the export takes the same rows the preview took and plans them
/// again. That is the property, not a workaround: the document and the screen come from one plan, so
/// a file printed for the archive cannot describe a different population from the one that was
/// applied. Same guarantee the évaluation import, the déliberation and <c>CnpnTargetPlanner</c> make,
/// and for the same reason.</para>
///
/// <para><b>It reads and writes nothing.</b> Exporting is not applying: a report can be cut before
/// the confirmation, after it, or a week later from the same file, and none of those touches a row.
/// It is safe to run against a roll that would be refused, which is exactly when it is most useful —
/// « donne-moi la liste des erreurs » is the request, and a refusal that only names the first line
/// cannot answer it.</para>
///
/// <para>⚠ <b>No cap, and no <c>ExportErrors.TooManyRows</c>.</b> The other two exports are scoped by
/// a year the caller may omit, so they can in principle be widened until they pull the base into
/// memory; this one is bounded by the uploaded file plus one academic year's registrations — 6 862
/// and ~8 000 on the largest real case. There is no axis to narrow and nothing to narrow it against,
/// so a limit here could only refuse a document the user has no other way to obtain.</para>
/// </remarks>
public sealed record GetReinscriptionSheetExportQuery(
    IReadOnlyList<ReinscriptionSheetRow> Rows,
    int FromAcademicYearId,
    int ToAcademicYearId) : IQuery<ExportFile>;

internal sealed class GetReinscriptionSheetExportQueryHandler(
    ReinscriptionSheetPlanner planner,
    IExportWorkbookWriter writer,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetReinscriptionSheetExportQuery, ExportFile>
{
    public async Task<Result<ExportFile>> Handle(
        GetReinscriptionSheetExportQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ExportErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ExportFile>(access.Error);

        var plan = await planner.PlanAsync(
            request.FromAcademicYearId, request.ToAcademicYearId, request.Rows, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<ExportFile>(plan.Error);

        var workbook = ReinscriptionSheetWorkbook.Build(plan.Value);

        return new ExportFile(workbook.FileName, writer.Write(workbook));
    }
}
