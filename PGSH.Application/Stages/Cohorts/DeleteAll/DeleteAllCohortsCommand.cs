using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.DeleteAll;

/// <summary>
/// « Réinitialiser les cohortes » — removes every cohorte a stage holds for one year, with the
/// affectations and périodes built on them.
/// </summary>
/// <remarks>
/// ⚠ <b>The year is resolved, never omitted.</b> A stage keeps a cohorte per (groupe, année) and
/// « CHIRURGIE » has 563 of them across six years, so a null year used to mean "every year this stage
/// ever ran" — on the one command in this area that deletes rows. An omitted year is the current one,
/// exactly as everywhere else.
/// </remarks>
public sealed record DeleteAllCohortsCommand(int StageId, int? AcademicYearId = null)
    : ICommand<DeleteAllCohortsResult>, IAuditableCommand
{
    public string AuditAction => "STAGE_COHORTS_RESET";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    /// <summary>
    /// ⚠ L'année demandée, qui peut être absente — « l'année en cours ». Celle que l'acte a
    /// réellement touchée est écrite par le handler, qui est le seul à l'avoir résolue : une entrée
    /// disant <c>null</c> ne permettrait pas de savoir quelle promotion a été réinitialisée.
    /// </summary>
    public string? AuditMetadata => AcademicYearId is null
        ? null
        : AuditMetadataJson.Of(("requestedAcademicYearId", AcademicYearId.Value));
}

/// <param name="CohortsRemoved">Cohortes deleted.</param>
/// <param name="AffectationsRemoved">Affectations that hung off them.</param>
/// <param name="PeriodsRemoved">Périodes de service that hung off those affectations.</param>
public sealed record DeleteAllCohortsResult(
    int CohortsRemoved, int AffectationsRemoved, int PeriodsRemoved);
