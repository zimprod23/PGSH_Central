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
/// The windows declared for an academic year, optionally narrowed to one promotion.
/// </summary>
/// <remarks>
/// ⚠ <b>Scoped by year, and an omitted year is the current one.</b> A pause is year-constituted — it
/// exists inside the dates of the year it names — so a read that left the year out would list every
/// exam session the faculty ever declared, which is the shape that returned 681 cohortes for one stage.
/// </remarks>
public sealed record GetPromotionPausesQuery(
    int? AcademicYearId = null,
    int? LevelId = null,
    int PageNumber = 1,
    int PageSize = 50) : IQuery<PaginatedResponse<PromotionPauseResponse>>;

internal sealed class GetPromotionPausesQueryValidator : AbstractValidator<GetPromotionPausesQuery>
{
    public GetPromotionPausesQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsAPageNumber();
        RuleFor(x => x.PageSize).IsAPageSize();
    }
}

internal sealed class GetPromotionPausesQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    WorkingDayProvider workingDays)
    : IQueryHandler<GetPromotionPausesQuery, PaginatedResponse<PromotionPauseResponse>>
{
    public async Task<Result<PaginatedResponse<PromotionPauseResponse>>> Handle(
        GetPromotionPausesQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PaginatedResponse<PromotionPauseResponse>>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var query = dbContext.PromotionPauses
            .AsNoTracking()
            .Where(p => p.AcademicYearId == yearId)
            .Where(p => request.LevelId == null || p.LevelId == request.LevelId)
            .OrderBy(p => p.StartDate)
            .ThenBy(p => p.LevelId);

        var page = await query.ToPaginatedResponseAsync(
            request.PageNumber,
            request.PageSize,
            p => new PauseRow(
                p.Id, p.LevelId, p.Level.Label, p.Level.Year, p.Level.AcademicProgram,
                p.StartDate, p.EndDate, p.Kind, p.Reason, p.IsConfirmed, p.RecordedOn),
            cancellationToken);

        // ⚠ The faculty calendar, never the promotion's own: measured against a calendar that already
        // contains the window, every window costs zero. Two pauses of one promotion cannot overlap, so
        // this is the exact figure and not an approximation.
        var calendar = await workingDays.BuildAsync(cancellationToken);

        // ⚠ What a window *costs* and what it *hits* are two different facts, and the row carried only
        // the first. « 29 ouvrables perdus » beside nothing else reads as an accounting note; the
        // number that says a plan is being cut is the count of columns and rotations underneath it —
        // and that number lived solely in the preview, behind a button, which is why a declared window
        // looked like it had no effect at all.
        var spans = await PromotionPauseQueries
            .PauseSpansQuery(dbContext, yearId, request.LevelId)
            .AsNoTracking()
            .ToDictionaryAsync(r => r.PauseId, cancellationToken);

        return new PaginatedResponse<PromotionPauseResponse>(
            page.Items.Select(r => Map(r, yearId, yearLabel, calendar, spans)).ToList(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount);
    }

    private static PromotionPauseResponse Map(
        PauseRow row,
        int yearId,
        string yearLabel,
        WorkingDayCalendar calendar,
        IReadOnlyDictionary<int, PromotionPauseQueries.PauseSpanRow> spans)
    {
        // A window declared on a promotion with no axis has no row here, and « no columns » is exactly
        // what that means — not « unknown ».
        var span = spans.GetValueOrDefault(row.Id);

        return new PromotionPauseResponse(
            row.Id,
            yearId,
            yearLabel,
            row.LevelId,
            ExportLabels.Level(row.LevelLabel, row.LevelYear, row.Program),
            row.StartDate,
            row.EndDate,
            row.EndDate.DayNumber - row.StartDate.DayNumber + 1,
            calendar.Count(row.StartDate, row.EndDate),
            span?.SlotsSpanning ?? 0,
            span?.PeriodsSpanning ?? 0,
            row.Kind,
            row.Reason,
            row.IsConfirmed,
            row.RecordedOn);
    }

    /// <summary>
    /// The row as the store gives it. Separate from <see cref="PromotionPauseResponse"/> because
    /// <c>WorkingDaysLost</c> is arithmetic on a calendar and the paginating selector is an
    /// <c>Expression</c> the provider has to translate.
    /// </summary>
    internal sealed record PauseRow(
        int Id,
        int LevelId,
        string? LevelLabel,
        int LevelYear,
        AcademicProgram Program,
        DateOnly StartDate,
        DateOnly EndDate,
        PauseKind Kind,
        string Reason,
        bool IsConfirmed,
        DateTime RecordedOn);
}
