using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Effectivity;

/// <summary>
/// Works out what applying an effectivity rule to registrations that <i>already exist</i> would do,
/// and — on the apply — does exactly that, from the same objects.
///
/// <para><b>Why this exists at all.</b> The rule normally needs no apply: it is read as each
/// registration is created, so authoring it in August and rolling the year over in September is the
/// whole mechanism. It is the other order that needs a hand — the réinscription ran, and only
/// afterwards did the faculty settle where the line falls. Without this, the only way to move those
/// registrations is SQL.</para>
///
/// <para>⚠ <b>A pronounced year is refused, never forced.</b> The verdict was recorded against a
/// requirement set; changing that set afterwards leaves nobody able to say what the jury ruled on.
/// There is no override flag, because there is no reading of the data under which one would be
/// right — re-opening the year is the act that makes the change legitimate, and it is deliberate.</para>
/// </summary>
internal sealed class CnpnEffectivityPlanner(
    IApplicationDbContext dbContext,
    RegistrationCnpnStamper stamper)
{
    private const int SampleSize = 50;

    internal sealed record Plan(CnpnEffectivityApplyPreview Preview, IReadOnlyList<Registration> Work);

    public async Task<Result<Plan>> PlanAsync(int effectivityId, CancellationToken ct)
    {
        var rule = await dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => e.Id == effectivityId)
            .Select(e => new
            {
                e.Id,
                e.CnpnVersionId,
                e.LevelId,
                Code = e.CnpnVersion.Code,
                LevelLabel = e.Level.Label,
                YearLabel = e.FromAcademicYear.Label,
                From = e.FromAcademicYear.StartDate,
            })
            .FirstOrDefaultAsync(ct);

        if (rule is null)
            return Result.Failure<Plan>(CnpnErrors.EffectivityNotFound(effectivityId));

        // ⚠ Bounded above by the next rule on this level, not left open-ended. Uniqueness is
        // (CnpnVersionId, LevelId) and (LevelId, FromAcademicYearId), so a level may carry several
        // rules — « à partir de 2026-2027 » followed by « à partir de 2028-2029 » — and
        // RegistrationCnpnStamper resolves a year to the rule with the *latest* From at or before it.
        // Read open-ended, this rule's preview claimed every later year too, and the apply then wrote
        // the later rule's text to them: the numbers the operator confirmed described writes that
        // never happened, which is exactly the « le dry run est le plan » guarantee this planner
        // exists to keep.
        var nextFrom = await dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => e.LevelId == rule.LevelId && e.FromAcademicYear.StartDate > rule.From)
            .OrderBy(e => e.FromAcademicYear.StartDate)
            .Select(e => (DateOnly?)e.FromAcademicYear.StartDate)
            .FirstOrDefaultAsync(ct);

        // Tracked: the apply writes through the aggregate, and the preview must plan against the same
        // objects it would mutate — the guarantee CnpnTargetPlanner and the évaluation import make.
        var inScope = await InScopeQuery(dbContext, rule.LevelId, rule.From, nextFrom).ToListAsync(ct);

        string levelLabel = rule.LevelLabel ?? $"niveau {rule.LevelId}";

        if (inScope.Count == 0)
            return new Plan(
                Empty(rule.Id, rule.Code, levelLabel, rule.YearLabel), []);

        var work = new List<Registration>();
        var sampled = new List<(Registration Registration, CnpnEffectivityRowStatus Status, string Message)>();
        int already = 0, willMove = 0, frozen = 0;

        foreach (var registration in inScope.OrderBy(r => r.AcademicYearId))
        {
            if (registration.CnpnVersionId == rule.CnpnVersionId)
            {
                already++;
                continue;
            }

            if (registration.OutcomeSource is not null)
            {
                frozen++;
                if (sampled.Count < SampleSize)
                    sampled.Add((registration, CnpnEffectivityRowStatus.FrozenByOutcome,
                        $"Année déjà prononcée ({registration.Status}) — le CNPN qui l'a régie ne "
                        + "peut plus changer."));
                continue;
            }

            willMove++;
            work.Add(registration);

            if (sampled.Count < SampleSize)
                sampled.Add((registration, CnpnEffectivityRowStatus.WillMove,
                    $"Sera rattachée au CNPN {rule.Code}."));
        }

        // ⚠ Read for the rows that will actually be printed, never for the whole scope. The scope is
        // a *described* set — one level over a window of years, ~900 registrations for a single
        // promotion — and the report shows at most SampleSize of them; the ids here are the ones the
        // loop just chose, so this is the bounded list `Contains` is the right tool for.
        var detail = await RowDetailQuery(dbContext, [.. sampled.Select(s => s.Registration.Id)])
            .ToDictionaryAsync(d => d.RegistrationId, ct);

        var sample = sampled
            .Select(s => Row(s.Registration, detail.GetValueOrDefault(s.Registration.Id), s.Status, s.Message))
            .ToList();

        int studentsMoved = work
            .Select(r => r.StudentId)
            .Distinct()
            .Count();

        var preview = new CnpnEffectivityApplyPreview(
            rule.Id, rule.Code, levelLabel, rule.YearLabel,
            InScope: inScope.Count,
            AlreadyGoverned: already,
            WillMove: willMove,
            FrozenByOutcome: frozen,
            StudentsMoved: studentsMoved,
            CanApply: willMove > 0,
            Sample: sample,
            SampleTotal: willMove + frozen);

        return new Plan(preview, work);
    }

    /// <summary>
    /// Runs the stamper over the planned registrations. The rule is already on disk, so the stamper
    /// resolves it as <c>Effectivity</c> and advances each student's own stamp — one implementation
    /// of the resolution, used by creation and by re-stamping alike.
    /// </summary>
    public Task<RegistrationCnpnStamper.StampReport> StampAsync(
        IReadOnlyList<Registration> work, CancellationToken ct) =>
        stamper.StampAsync(work, ct);

    /// <summary>
    /// The registrations a rule reaches: one level, from the year it takes effect up to the year the
    /// <i>next</i> rule on that level does. Named and static so <c>SqlTranslationTests</c> can compile
    /// it — a query buried in a private async method cannot be handed to <c>ToQueryString()</c>, and
    /// the in-memory provider translates nothing.
    /// </summary>
    internal static IQueryable<Registration> InScopeQuery(
        IApplicationDbContext dbContext, int levelId, DateOnly from, DateOnly? nextFrom) =>
        dbContext.Registrations
            .Where(r => r.LevelId == levelId
                     && r.AcademicYear.StartDate >= from
                     && (nextFrom == null || r.AcademicYear.StartDate < nextFrom));

    /// <summary>What the report prints beside each sampled registration.</summary>
    internal static IQueryable<RowDetail> RowDetailQuery(
        IApplicationDbContext dbContext, IReadOnlyList<Guid> registrationIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => registrationIds.Contains(r.Id))
            .Select(r => new RowDetail(
                r.Id,
                r.StudentId,
                r.Student.FirstName + " " + r.Student.LastName,
                r.Student.CNE,
                r.AcademicYear.Label,
                r.CnpnVersion != null ? r.CnpnVersion.Code : null));

    internal sealed record RowDetail(
        Guid RegistrationId,
        Guid StudentId,
        string FullName,
        string? Cne,
        string YearLabel,
        string? CurrentCode);

    private static CnpnEffectivityRow Row(
        Registration registration, RowDetail? detail, CnpnEffectivityRowStatus status, string message) =>
        new(registration.Id,
            registration.StudentId,
            detail?.FullName.Trim() ?? "—",
            detail?.Cne,
            detail?.YearLabel ?? "—",
            detail?.CurrentCode,
            status,
            message);

    private static CnpnEffectivityApplyPreview Empty(
        int id, string code, string levelLabel, string yearLabel) =>
        new(id, code, levelLabel, yearLabel, 0, 0, 0, 0, 0, CanApply: false, [], 0);
}
