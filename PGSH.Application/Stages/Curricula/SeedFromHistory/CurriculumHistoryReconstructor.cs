using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.SeedFromHistory;

/// <summary>
/// Rebuilds past CNPN records from what was actually served: the set of stages the cohorts of a level
/// belonged to, attributed to the text that governed the intake which reached that level.
///
/// <para>
/// The rule lives here rather than in the command handler because it has two callers — the
/// authenticated endpoint, and the migration tooling that lands a legacy database and then needs the
/// curricula derived from it in the same pass. Authorisation stays with the handler; this is only the
/// derivation.
/// </para>
///
/// <para>
/// It is an approximation, and the only one available: before the <see cref="Curriculum"/> aggregate
/// existed nothing recorded the requirement set, so execution is the sole surviving evidence. It
/// under-reports — a stage the text required but which no group ran that year leaves no trace, which
/// is why the years attributed to one version are unioned rather than made to compete. Curricula
/// already recorded are never touched, so a set confirmed by hand survives a re-run.
/// </para>
/// </summary>
public sealed class CurriculumHistoryReconstructor(
    IApplicationDbContext dbContext,
    CnpnAssignment assignment)
{
    public async Task<Result<CurriculumSeedReport>> ReconstructAsync(bool dryRun, CancellationToken ct)
    {
        var served = await dbContext.Cohorts
            .AsNoTracking()
            .Select(c => new
            {
                LevelId        = c.Stage.LevelId,
                LevelYear      = c.Stage.Level.Year,
                Program        = c.Stage.Level.AcademicProgram,
                AcademicYearId = c.AcademicGroup.AcademicYearId,
                c.StageId,
                c.Stage.Coefficient,
                c.Stage.DurationInDays,
            })
            .Distinct()
            .ToListAsync(ct);

        var alreadyRecorded = (await dbContext.Curriculums
                .AsNoTracking()
                .Select(c => new { c.LevelId, c.CnpnVersionId })
                .ToListAsync(ct))
            .Select(c => (c.LevelId, c.CnpnVersionId))
            .ToHashSet();

        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .OrderBy(y => y.StartDate)
            .Select(y => y.Id)
            .ToListAsync(ct);

        // Which text a served (level, year) belongs to is decided by the intake that reached it:
        // an on-time student at level L in year Y entered L-1 years earlier. Several years therefore
        // collapse onto one version, and their stage sets are unioned rather than fought over — the
        // reconstruction under-reports (a stage no group ran that year leaves no trace), so the union
        // recovers more of the text, not less.
        var byVersion = new Dictionary<(int LevelId, int VersionId), Dictionary<int, (int Coefficient, int Duration)>>();

        foreach (var row in served)
        {
            int index = years.IndexOf(row.AcademicYearId);
            if (index < 0) continue;

            int entryYearId = years[Math.Max(0, index - Math.Max(0, row.LevelYear - 1))];

            var version = await assignment.SelectVersionAsync(row.Program, entryYearId, ct);
            if (version.IsFailure) continue;   // no text reaches that far back; nothing to attribute

            var key = (row.LevelId, version.Value);
            if (!byVersion.TryGetValue(key, out var stages))
                byVersion[key] = stages = [];

            // Keep the heaviest reading when years disagree: a text that reweighted a stage upward is
            // better represented by the larger figure than by whichever year happened to sort last.
            if (!stages.TryGetValue(row.StageId, out var current)
                || row.Coefficient > current.Coefficient
                || row.DurationInDays > current.Duration)
            {
                stages[row.StageId] = (
                    Math.Max(row.Coefficient, current.Coefficient),
                    Math.Max(row.DurationInDays, current.Duration));
            }
        }

        var details = new List<string>();
        int created = 0, entries = 0, skipped = 0;

        foreach (var ((levelId, versionId), stages) in byVersion
            .OrderBy(g => g.Key.LevelId)
            .ThenBy(g => g.Key.VersionId))
        {
            if (alreadyRecorded.Contains((levelId, versionId)))
            {
                skipped++;
                continue;
            }

            var curriculum = new Curriculum
            {
                LevelId       = levelId,
                CnpnVersionId = versionId,
                Reference     = "Reconstitué depuis les stages effectivement servis",
            };

            foreach (var (stageId, weights) in stages.OrderBy(s => s.Key))
            {
                var result = curriculum.AddStage(stageId, weights.Coefficient, weights.Duration);
                if (result.IsFailure)
                    return Result.Failure<CurriculumSeedReport>(result.Error);

                entries++;
            }

            created++;
            details.Add($"Niveau {levelId}, CNPN {versionId} : {stages.Count} stage(s).");

            if (!dryRun)
                dbContext.Curriculums.Add(curriculum);
        }

        if (!dryRun && created > 0)
            await dbContext.SaveChangesAsync(ct);

        return new CurriculumSeedReport(dryRun, created, entries, skipped, details);
    }
}
