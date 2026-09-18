using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Exports;
using PGSH.Application.Extensions;
using PGSH.Domain.Calendar;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// « Les rotations que je suis sur le point de démarrer traversent-elles une fenêtre déclarée ? »
/// asked <b>before</b> the act, with the same scoping the act itself uses.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Why a read and not a guard.</b> Starting a rotation that spans an exam window is not
/// wrong — the student serves before it and after it. What is wrong is <i>silent</i>: the rotation's
/// end date does not grow to replace the days the window takes, so the stay is short by exactly that
/// much and nothing on the screen says so. This is therefore a sentence, not a refusal, and it follows
/// the settled rule that a measured shortfall is <b>shown</b> rather than enforced — the same bargain
/// capacity got on 12/09/2026.</para>
///
/// <para>⚠ <b>The scoping mirrors <c>StagePeriodRunner.StartStageAsync</c> exactly</b>, down to the
/// period-number filter reaching through the grid cell: a report drawn from a wider or narrower
/// selection than the button acts on is worse than no report, because it is believed. The two are
/// deliberately written next to each other's rules — if the runner's scoping moves, this moves with it.</para>
///
/// <para>⚠ <b>The promotion comes from the <em>registration</em>, not from the stage's level.</b> A
/// sixth-year re-taking a third-year stage sits his own promotion's exams. This is the same choice
/// <see cref="PromotionPauseQueries.PeriodsQuery"/> documents, and the two must not drift.</para>
/// </remarks>
public sealed record GetStagePauseCrossingsQuery(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<int>? CohortIds = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : IQuery<StagePauseCrossingsResponse>;

internal sealed class GetStagePauseCrossingsQueryValidator
    : AbstractValidator<GetStagePauseCrossingsQuery>
{
    // ⚠ StageId is bound from the *route*, not the query string, so it cannot be omitted and needs no
    // RequiredParameterRules treatment — the route constraint refuses a non-integer before binding. What
    // is worth refusing in words is a meaningless one, which an int still lets a caller write.
    public GetStagePauseCrossingsQueryValidator() =>
        RuleFor(x => x.StageId)
            .GreaterThan(0)
            .WithMessage("Le stage est obligatoire.");
}

internal sealed class GetStagePauseCrossingsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    WorkingDayProvider workingDays)
    : IQueryHandler<GetStagePauseCrossingsQuery, StagePauseCrossingsResponse>
{
    public async Task<Result<StagePauseCrossingsResponse>> Handle(
        GetStagePauseCrossingsQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<StagePauseCrossingsResponse>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.Name, s.LevelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (stage is null)
            return Result.Failure<StagePauseCrossingsResponse>(StageErrors.NotFound(request.StageId));

        var toStart = PromotionPauseQueries.PeriodsAboutToStartQuery(
            dbContext, request.StageId, yearId,
            request.CohortIds, request.PartitionLabels, request.PeriodNumbers);

        int periodsToStart = await toStart.CountAsync(cancellationToken);

        // The promotions to ask about are the stage's own **and** every one actually represented in the
        // selection — a sixth-year re-taking this stage sits his own promotion's exams.
        //
        // ⚠ <b>The stage's level is in the union rather than derived from the selection alone, and that
        // is not a belt-and-braces addition.</b> Drawn from the selection only, an empty selection —
        // every rotation already started, which is the ordinary state of a stage under way — yields no
        // promotions, hence no windows, hence WindowsDeclaredForPromotion = 0: the screen would say
        // « aucune fenêtre déclarée » while one was sitting on the promotion. That is precisely the
        // silence-read-as-safety this field exists to refuse, so it must not be reachable from inside
        // the fix for it. Caught by A_rotation_already_started_is_not_about_to_start.
        var selectionLevelIds = await toStart
            .Select(p => p.InternshipAssignment.Registration.LevelId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var levelIds = selectionLevelIds.Append(stage.LevelId).Distinct().ToList();

        var declared = await dbContext.PromotionPauses
            .AsNoTracking()
            .Where(p => p.AcademicYearId == yearId && levelIds.Contains(p.LevelId))
            .OrderBy(p => p.StartDate)
            .Select(p => new PauseRow(
                p.Id, p.LevelId, p.Level.Label, p.Level.Year, p.Level.AcademicProgram,
                p.StartDate, p.EndDate, p.Kind, p.Reason, p.IsConfirmed))
            .ToListAsync(cancellationToken);

        // ⚠ The faculty calendar, as the list uses — never the promotion's own, which already contains
        // the window and would price every one of them at zero.
        var calendar = await workingDays.BuildAsync(cancellationToken);

        var windows = new List<CrossedWindowResponse>();
        foreach (var row in declared)
        {
            int crossing = await toStart
                .Where(p => p.StartDate <= row.EndDate && p.EndDate >= row.StartDate)
                .CountAsync(cancellationToken);

            if (crossing == 0)
                continue;

            windows.Add(new CrossedWindowResponse(
                row.Id,
                row.LevelId,
                ExportLabels.Level(row.LevelLabel, row.LevelYear, row.Program),
                row.StartDate,
                row.EndDate,
                row.Kind,
                row.Reason,
                row.IsConfirmed,
                calendar.Count(row.StartDate, row.EndDate),
                crossing));
        }

        int periodsCrossing = await PromotionPauseQueries
            .CrossingADeclaredWindowQuery(dbContext, toStart, yearId)
            .CountAsync(cancellationToken);

        return new StagePauseCrossingsResponse(
            request.StageId,
            stage.Name,
            yearId,
            yearLabel,
            periodsToStart,
            periodsCrossing,
            declared.Count,
            windows);
    }

    private sealed record PauseRow(
        int Id,
        int LevelId,
        string? LevelLabel,
        int LevelYear,
        AcademicProgram Program,
        DateOnly StartDate,
        DateOnly EndDate,
        PauseKind Kind,
        string Reason,
        bool IsConfirmed);
}
