using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Curricula.SeedFromHistory;

/// <summary>
/// Derives past CNPN records from what was actually served: the stages a level's cohorts belonged to,
/// attributed to the text governing the intake that reached that level.
///
/// <para>
/// This is an approximation and the only one available — before this feature the requirement set was
/// never recorded, so execution is the sole surviving evidence. It under-reports: a stage the text
/// required but which no group ran leaves no trace, which is why the years attributed to one text are
/// unioned rather than made to compete. Existing curricula are never touched, so a set confirmed by
/// hand is safe from re-runs.
/// </para>
/// </summary>
public sealed record SeedCurriculaFromHistoryCommand(bool DryRun = true)
    : ICommand<CurriculumSeedReport>, IAuditableCommand
{
    public string  AuditAction     => "CURRICULA_SEEDED_FROM_HISTORY";
    public string  AuditEntityType => "Curriculum";
    public string? AuditEntityId   => null;
    public string? AuditMetadata   => $"{{\"dryRun\":{DryRun.ToString().ToLowerInvariant()}}}";
}

public sealed record CurriculumSeedReport(
    bool DryRun,
    int  CurriculaCreated,
    int  StageEntriesCreated,
    int  CurriculaSkippedBecauseTheyExist,
    IReadOnlyList<string> Details);
