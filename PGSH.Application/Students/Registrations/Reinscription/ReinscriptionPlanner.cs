using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// Derives the next year's registrations from a promotion's closed verdicts, and reports what it
/// cannot derive and why. Preview and apply both run this and nothing else, so the dry run is the plan.
///
/// <para><b>Scoped to one promotion</b>, like the déliberation canvas it follows: one (year, level) in,
/// one or two (year, level) out. Unscoped it would return every student of every level in one
/// unpaginated response — the trap a single-object response hides.</para>
///
/// <para><b>Idempotent and additive, not all-or-nothing.</b> This is a deliberate difference from the
/// déliberation import. A student who already holds a registration in the target year is skipped, so
/// the rollover can be re-run after the two odd verdicts are corrected; refusing all 690 over three
/// anomalies would buy nothing, because re-running is safe. The déliberation cannot work that way — its
/// file is not stored, so a half-applied promotion could not be reconstructed.</para>
/// </summary>
internal sealed class ReinscriptionPlanner(IApplicationDbContext dbContext)
{
    public async Task<Result<ReinscriptionPlan>> PlanAsync(
        int fromAcademicYearId,
        int toAcademicYearId,
        int levelId,
        CancellationToken ct)
    {
        if (fromAcademicYearId == toAcademicYearId)
            return Result.Failure<ReinscriptionPlan>(ReinscriptionErrors.SameYear);

        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == fromAcademicYearId || y.Id == toAcademicYearId)
            .Select(y => new { y.Id, y.Label, y.StartDate })
            .ToListAsync(ct);

        var fromYear = years.FirstOrDefault(y => y.Id == fromAcademicYearId);
        var toYear = years.FirstOrDefault(y => y.Id == toAcademicYearId);

        if (fromYear is null)
            return Result.Failure<ReinscriptionPlan>(StageErrors.AcademicYearNotFound(fromAcademicYearId));
        if (toYear is null)
            return Result.Failure<ReinscriptionPlan>(StageErrors.AcademicYearNotFound(toAcademicYearId));

        if (toYear.StartDate <= fromYear.StartDate)
            return Result.Failure<ReinscriptionPlan>(ReinscriptionErrors.TargetYearNotLater);

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (level is null)
            return Result.Failure<ReinscriptionPlan>(RegistrationErrors.MissingLevel);

        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        // The level a student who cleared the year moves up to. Absent for the last year of a
        // programme, which is what NextLevelMissing reports.
        var nextLevel = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.AcademicProgram == level.AcademicProgram && l.Year == level.Year + 1)
            .Select(l => new { l.Id, l.Label, l.Year })
            .FirstOrDefaultAsync(ct);

        var promotion = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == fromAcademicYearId && r.LevelId == levelId)
            .Select(r => new PromotionMember(
                r.StudentId,
                ((r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? "")).Trim(),
                r.Student.CNE,
                r.Status,
                r.OutcomeSource))
            .ToListAsync(ct);

        if (promotion.Count == 0)
            return Result.Failure<ReinscriptionPlan>(
                ReinscriptionErrors.PromotionHasNoStudents(levelLabel, fromYear.Label));

        // Who already has a registration in the target year — the students a re-run must leave alone.
        var studentIds = promotion.Select(m => m.StudentId).ToList();
        var alreadyRegistered = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => r.AcademicYearId == toAcademicYearId && studentIds.Contains(r.StudentId))
                .Select(r => r.StudentId)
                .ToListAsync(ct))
            .ToHashSet();

        string nextLevelLabel = nextLevel is null
            ? ""
            : nextLevel.Label ?? $"Année {nextLevel.Year} — {level.AcademicProgram}";

        var reports = new List<ReinscriptionRowReport>(promotion.Count);
        var work = new List<PlannedRegistration>();

        foreach (var member in promotion)
        {
            var resolved = Resolve(member, alreadyRegistered, level.Id, levelLabel,
                nextLevel?.Id, nextLevelLabel);

            reports.Add(resolved.Report);
            if (resolved.Work is { } planned) work.Add(planned);
        }

        return new ReinscriptionPlan(
            Summarize(fromYear.Label, toYear.Label, levelLabel, reports),
            toAcademicYearId,
            work);
    }

    private sealed record Resolution(ReinscriptionRowReport Report, PlannedRegistration? Work);

    private static Resolution Resolve(
        PromotionMember member,
        IReadOnlySet<Guid> alreadyRegistered,
        int sameLevelId,
        string sameLevelLabel,
        int? nextLevelId,
        string nextLevelLabel)
    {
        // Order matters: an existing registration in the target year outranks everything, so a re-run
        // reports "already there" rather than re-deriving a verdict that may since have been corrected.
        if (alreadyRegistered.Contains(member.StudentId))
            return Report(member, ReinscriptionAction.AlreadyRegistered, null,
                "Déjà inscrit pour l'année de destination.");

        if (member.OutcomeSource is null || !member.Status.IsYearOutcome())
            return Report(member, ReinscriptionAction.NoOutcome, null,
                "Aucune décision enregistrée pour cette année — clôturez la promotion d'abord.");

        if (member.Status.EndsTheCursus())
            return Report(member, ReinscriptionAction.CursusEnded, null,
                member.Status switch
                {
                    RegistrationStatus.Graduated => "Diplômé — fin du cursus.",
                    RegistrationStatus.Excluded => "Exclu — fin du cursus.",
                    _ => "Abandon — fin du cursus.",
                });

        if (member.Status == RegistrationStatus.Failed)
            return Register(member, sameLevelId, sameLevelLabel,
                $"Redoublant — réinscrit en « {sameLevelLabel} ».");

        // Admis. The only remaining outcome, and the only one that needs a level above this one.
        if (nextLevelId is null)
            return Report(member, ReinscriptionAction.NextLevelMissing, null,
                "Admis, mais aucun niveau supérieur n'existe pour ce programme — "
                + "la décision attendue est vraisemblablement « Diplômé ».");

        return Register(member, nextLevelId.Value, nextLevelLabel,
            $"Admis — réinscrit en « {nextLevelLabel} ».");
    }

    private static Resolution Register(
        PromotionMember member, int levelId, string levelLabel, string message) =>
        new(new ReinscriptionRowReport(
                member.StudentId, member.FullName, member.Cne, member.Status, member.OutcomeSource,
                ReinscriptionAction.WillRegister, levelLabel, message),
            new PlannedRegistration(member.StudentId, levelId, levelLabel));

    private static Resolution Report(
        PromotionMember member, ReinscriptionAction action, string? toLevelLabel, string message) =>
        new(new ReinscriptionRowReport(
                member.StudentId, member.FullName, member.Cne, member.Status, member.OutcomeSource,
                action, toLevelLabel, message),
            null);

    private static ReinscriptionReport Summarize(
        string fromYearLabel,
        string toYearLabel,
        string levelLabel,
        IReadOnlyList<ReinscriptionRowReport> rows)
    {
        int willRegister = rows.Count(r => r.Action == ReinscriptionAction.WillRegister);

        var byTargetLevel = rows
            .Where(r => r.Action == ReinscriptionAction.WillRegister && r.ToLevelLabel is not null)
            .GroupBy(r => r.ToLevelLabel!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ReinscriptionReport(
            fromYearLabel, toYearLabel, levelLabel,
            TotalConsidered: rows.Count,
            WillRegister: willRegister,
            Skipped: rows.Count - willRegister,
            NeedsAttention: rows.Count(r => r.Action
                is ReinscriptionAction.NoOutcome or ReinscriptionAction.NextLevelMissing),
            byTargetLevel,
            rows);
    }

    private sealed record PromotionMember(
        Guid StudentId,
        string FullName,
        string? Cne,
        RegistrationStatus Status,
        RegistrationOutcomeSource? OutcomeSource);
}

internal sealed record ReinscriptionPlan(
    ReinscriptionReport Report,
    int ToAcademicYearId,
    IReadOnlyList<PlannedRegistration> Work);

internal sealed record PlannedRegistration(Guid StudentId, int LevelId, string LevelLabel);
