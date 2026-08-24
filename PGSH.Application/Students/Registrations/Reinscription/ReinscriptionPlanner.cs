using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// Derives the next year's registrations from the closed verdicts of a year, and reports what it
/// cannot derive and why. Preview and apply both run this and nothing else, so the dry run is the plan.
///
/// <para><b>Scoped to one academic year</b>, optionally narrowed to one level — the same scoping as the
/// déliberation canvas it follows, and for the same reason: a whole year is closed in one sitting, so
/// making the rollover follow it level by level only invites half of it to be forgotten. Each student
/// moves up from <em>his own</em> level, so a year-wide run is a set of independent promotions, not a
/// merged one.</para>
///
/// <para><b>Idempotent and additive, not all-or-nothing.</b> This is a deliberate difference from the
/// déliberation import. A student who already holds a registration in the target year is skipped, so
/// the rollover can be re-run after the odd verdicts are corrected; refusing all 690 over three
/// anomalies would buy nothing, because re-running is safe. The déliberation cannot work that way — its
/// file is not stored, so a half-applied promotion could not be reconstructed.</para>
/// </summary>
internal sealed class ReinscriptionPlanner(
    IApplicationDbContext dbContext,
    OutstandingStageFinder outstandingStages)
{
    /// <summary>
    /// A year-wide rollover considers every student of the faculty, and the reply is a single object.
    /// Rows needing attention are ordered first, so the cap never hides one.
    /// </summary>
    public const int MaxReportedRows = 1000;

    public async Task<Result<ReinscriptionPlan>> PlanAsync(
        int fromAcademicYearId,
        int toAcademicYearId,
        int? levelId,
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

        // The whole catalogue: a dozen rows, and both the level a student sits in and the one above it
        // are read from it. Loading it once beats a query per promotion on a year-wide run.
        var levels = (await dbContext.Levels
                .AsNoTracking()
                .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
                .ToListAsync(ct))
            .Select(l => new LevelInfo(
                l.Id, l.Label ?? $"Année {l.Year} — {l.AcademicProgram}", l.Year, l.AcademicProgram))
            .ToList();

        var byId = levels.ToDictionary(l => l.Id);
        var nextOf = levels.ToDictionary(
            l => l.Id,
            l => levels.FirstOrDefault(n => n.Program == l.Program && n.Year == l.Year + 1));

        string scopeLabel = "Toutes les promotions";
        if (levelId is { } id)
        {
            if (!byId.TryGetValue(id, out var scoped))
                return Result.Failure<ReinscriptionPlan>(RegistrationErrors.MissingLevel);

            scopeLabel = scoped.Label;
        }

        var promotion = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == fromAcademicYearId)
            .Where(r => levelId == null || r.LevelId == levelId)
            .Select(r => new PromotionMember(
                r.StudentId,
                ((r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? "")).Trim(),
                r.Student.CNE,
                r.LevelId,
                r.Status,
                r.OutcomeSource))
            .ToListAsync(ct);

        if (promotion.Count == 0)
            return Result.Failure<ReinscriptionPlan>(
                ReinscriptionErrors.PromotionHasNoStudents(scopeLabel, fromYear.Label));

        // Who already has a registration in the target year — the students a re-run must leave alone.
        var studentIds = promotion.Select(m => m.StudentId).ToList();
        var alreadyRegistered = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => r.AcademicYearId == toAcademicYearId && studentIds.Contains(r.StudentId))
                .Select(r => r.StudentId)
                .ToListAsync(ct))
            .ToHashSet();

        // ⚠ Everything below is scoped by the same predicate that selected the promotion, never by
        // shipping its 8 000 student ids back down — see OutstandingStageFinder's remarks.
        var debts = await outstandingStages.ForPromotionAsync(fromAcademicYearId, levelId, ct);
        var finalYears = await FinalYearByStudentAsync(fromAcademicYearId, levelId, ct);
        var waived = await WaivedStudentsAsync(toAcademicYearId, fromAcademicYearId, levelId, ct);

        var reports = new List<ReinscriptionRowReport>(promotion.Count);
        var work = new List<PlannedRegistration>();
        int finalYearWaived = 0;

        foreach (var member in promotion)
        {
            var from = byId.GetValueOrDefault(member.LevelId)
                ?? new LevelInfo(member.LevelId, $"Niveau {member.LevelId}", 0, default);

            // ⚠ TryGetValue, not GetValueOrDefault. The dictionary holds `int`, so the default is 0,
            // not null — and a 0 read as "his text runs 0 years" makes *every* year his last, which
            // blocked every student PGSH holds no CNPN for. The one case the gate must stand aside
            // for was the one it fired hardest on.
            var gate = new FinalYearGate(
                finalYears.TryGetValue(member.StudentId, out int total) ? total : null,
                debts.GetValueOrDefault(member.StudentId, []),
                waived.Contains(member.StudentId));

            var resolved = Resolve(member, alreadyRegistered, from, nextOf.GetValueOrDefault(member.LevelId), gate);

            // Counted here rather than derived from the message: an override has to be visible in the
            // same report as the rule it bends, and a count read back out of prose is not a count.
            if (gate.Waived && gate.Debts.Count > 0
                && resolved.Report.Action == ReinscriptionAction.WillRegister)
                finalYearWaived++;

            reports.Add(resolved.Report);
            if (resolved.Work is { } planned) work.Add(planned);
        }

        return new ReinscriptionPlan(
            Summarize(fromYear.Label, toYear.Label, scopeLabel, reports, finalYearWaived),
            toAcademicYearId,
            work);
    }

    private sealed record Resolution(ReinscriptionRowReport Report, PlannedRegistration? Work);

    /// <summary>
    /// What decides whether a student may start his last year: how long his own text runs, what he
    /// still owes from earlier years, and whether the faculty has already excused it.
    /// </summary>
    private sealed record FinalYearGate(
        int? TotalYears,
        IReadOnlyList<OutstandingStageFinder.Debt> Debts,
        bool Waived);

    private static Resolution Resolve(
        PromotionMember member,
        IReadOnlySet<Guid> alreadyRegistered,
        LevelInfo from,
        LevelInfo? next,
        FinalYearGate gate)
    {
        // Order matters: an existing registration in the target year outranks everything, so a re-run
        // reports "already there" rather than re-deriving a verdict that may since have been corrected.
        if (alreadyRegistered.Contains(member.StudentId))
            return Report(member, from, ReinscriptionAction.AlreadyRegistered, null,
                "Déjà inscrit pour l'année de destination.");

        if (member.OutcomeSource is null || !member.Status.IsYearOutcome())
            return Report(member, from, ReinscriptionAction.NoOutcome, null,
                "Aucune décision enregistrée pour cette année — clôturez la promotion d'abord.");

        if (member.Status.EndsTheCursus())
            return Report(member, from, ReinscriptionAction.CursusEnded, null,
                member.Status switch
                {
                    RegistrationStatus.Graduated => "Diplômé — fin du cursus.",
                    RegistrationStatus.Excluded => "Exclu — fin du cursus.",
                    _ => "Abandon — fin du cursus.",
                });

        if (member.Status == RegistrationStatus.Failed)
            return Register(member, from, from,
                $"Redoublant — réinscrit en « {from.Label} ».");

        // Admis. The only remaining outcome, and the only one that needs a level above this one.
        if (next is null)
            return Report(member, from, ReinscriptionAction.NextLevelMissing, null,
                "Admis, mais aucun niveau supérieur n'existe pour ce programme — "
                + "la décision attendue est vraisemblablement « Diplômé ».");

        // ⚠ The gate. The last year of a cursus cannot begin while a stage from an earlier one is
        // unvalidated — 7ᵉ année under arrêté 2174.18, 6ᵉ under 1650.25. It is asked per *student*,
        // from his own text: from 2026-2027 one 6ᵉ année Médecine holds students of both, so the level
        // alone cannot answer "is this his last year?".
        //
        // Stands aside where no text is recorded, exactly as the déliberation's « Diplômé » rule does:
        // a student nobody has stamped must not be blocked by a number PGSH does not have.
        bool entersFinalYear = gate.TotalYears is { } totalYears && totalYears > 0 && next.Year >= totalYears;
        var owed = gate.Debts.Where(d => d.LevelYear < next.Year).ToList();

        if (entersFinalYear && owed.Count > 0 && !gate.Waived)
            return Report(member, from, ReinscriptionAction.FinalYearBlocked, next.Label,
                $"Admis, mais « {next.Label} » est sa dernière année et {owed.Count} stage(s) "
                + $"antérieur(s) ne sont pas validés — {OutstandingStageFinder.Summarize(owed)}. "
                + "Faites-les revalider, ou accordez une dérogation.");

        string note = entersFinalYear && owed.Count > 0
            ? $"Admis — réinscrit en « {next.Label} » par dérogation ({owed.Count} stage(s) antérieur(s) non validés)."
            : $"Admis — réinscrit en « {next.Label} ».";

        return Register(member, from, next, note);
    }

    private static Resolution Register(
        PromotionMember member, LevelInfo from, LevelInfo to, string message) =>
        new(new ReinscriptionRowReport(
                member.StudentId, member.FullName, member.Cne, from.Id, from.Label, member.Status,
                member.OutcomeSource, ReinscriptionAction.WillRegister, to.Label, message),
            new PlannedRegistration(member.StudentId, to.Id, to.Label));

    private static Resolution Report(
        PromotionMember member, LevelInfo from, ReinscriptionAction action,
        string? toLevelLabel, string message) =>
        new(new ReinscriptionRowReport(
                member.StudentId, member.FullName, member.Cne, from.Id, from.Label, member.Status,
                member.OutcomeSource, action, toLevelLabel, message),
            null);

    private static ReinscriptionReport Summarize(
        string fromYearLabel,
        string toYearLabel,
        string scopeLabel,
        IReadOnlyList<ReinscriptionRowReport> rows,
        int finalYearWaived)
    {
        int willRegister = rows.Count(r => r.Action == ReinscriptionAction.WillRegister);

        var byTargetLevel = rows
            .Where(r => r.Action == ReinscriptionAction.WillRegister && r.ToLevelLabel is not null)
            .GroupBy(r => r.ToLevelLabel!)
            .ToDictionary(g => g.Key, g => g.Count());

        var byLevel = rows
            .GroupBy(r => (r.FromLevelId, r.FromLevelLabel))
            .OrderBy(g => g.Key.FromLevelLabel, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ReinscriptionLevelBreakdown(
                LevelId: g.Key.FromLevelId,
                LevelLabel: g.Key.FromLevelLabel,
                Considered: g.Count(),
                WillRegister: g.Count(r => r.Action == ReinscriptionAction.WillRegister),
                NeedsAttention: g.Count(r => r.Action.NeedsAttention())))
            .ToList();

        // Attention first, so the cap can never hide the rows the operator has to act on.
        var ordered = rows
            .OrderByDescending(r => r.Action.NeedsAttention())
            .ThenBy(r => r.FromLevelLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StudentFullName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxReportedRows)
            .ToList();

        return new ReinscriptionReport(
            fromYearLabel, toYearLabel, scopeLabel,
            TotalConsidered: rows.Count,
            WillRegister: willRegister,
            Skipped: rows.Count - willRegister,
            NeedsAttention: rows.Count(r => r.Action.NeedsAttention()),
            FinalYearBlocked: rows.Count(r => r.Action == ReinscriptionAction.FinalYearBlocked),
            FinalYearWaived: finalYearWaived,
            byTargetLevel,
            byLevel,
            ordered,
            RowsTruncated: rows.Count > MaxReportedRows);
    }

    /// <summary>
    /// How long each student's own text runs. ⚠ Read from the <em>registration's</em> CNPN first and
    /// the student's stamp only as a fallback — the same order as everywhere else, and the reason is
    /// the same: once an effectivity rule can move a student mid-cursus, "how many years does he owe"
    /// stops being a property of where he stands today. Absent means no text on record, and the gate
    /// stands aside for him.
    /// </summary>
    private async Task<Dictionary<Guid, int>> FinalYearByStudentAsync(
        int yearId, int? levelId, CancellationToken ct)
    {
        var rows = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == yearId)
            .Where(r => levelId == null || r.LevelId == levelId)
            .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
            .Select(r => new
            {
                r.StudentId,
                TotalYears = r.CnpnVersionId != null
                    ? r.CnpnVersion!.TotalYears
                    : r.Student.CnpnVersion!.TotalYears,
            })
            .ToListAsync(ct);

        var map = new Dictionary<Guid, int>(rows.Count);
        foreach (var row in rows) map[row.StudentId] = row.TotalYears;
        return map;
    }

    /// <summary>
    /// Students the faculty has already allowed into their final year for the target year. Scoped by
    /// the promotion's own predicate rather than by its ids, like every other lookup here.
    /// </summary>
    private async Task<HashSet<Guid>> WaivedStudentsAsync(
        int toAcademicYearId, int fromAcademicYearId, int? levelId, CancellationToken ct)
    {
        var ids = await dbContext.FinalYearEntryWaivers
            .AsNoTracking()
            .Where(w => w.AcademicYearId == toAcademicYearId)
            .Where(w => dbContext.Registrations.Any(r =>
                r.StudentId == w.StudentId
                && r.AcademicYearId == fromAcademicYearId
                && (levelId == null || r.LevelId == levelId)))
            .Select(w => w.StudentId)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    private sealed record LevelInfo(int Id, string Label, int Year, AcademicProgram Program);

    private sealed record PromotionMember(
        Guid StudentId,
        string FullName,
        string? Cne,
        int LevelId,
        RegistrationStatus Status,
        RegistrationOutcomeSource? OutcomeSource);
}

internal sealed record ReinscriptionPlan(
    ReinscriptionReport Report,
    int ToAcademicYearId,
    IReadOnlyList<PlannedRegistration> Work);

internal sealed record PlannedRegistration(Guid StudentId, int LevelId, string LevelLabel);
