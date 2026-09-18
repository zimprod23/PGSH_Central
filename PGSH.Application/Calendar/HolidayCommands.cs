using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Calendar;

/// <summary>
/// Records a non-working stretch. The one that matters is <see cref="HolidayKind.Religious"/>: those dates
/// cannot be generated, so this command is how a year's calendar becomes complete enough to lay an axis on.
/// </summary>
/// <param name="CountsAsWorkingDay">
/// Whether the faculty works through it. Defaults to <c>false</c> — nobody serves — which is what every
/// row in the base says and what an omitted field must therefore mean.
/// </param>
public sealed record CreateHolidayCommand(
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed = true,
    bool CountsAsWorkingDay = false) : ICommand<int>, IAuditableCommand
{
    public string AuditAction => "HOLIDAY_CREATED";
    public string AuditEntityType => "Holiday";
    public string? AuditEntityId => null;
    public string? AuditMetadata =>
        $$"""{"name":"{{Name}}","from":"{{StartDate:yyyy-MM-dd}}","to":"{{EndDate:yyyy-MM-dd}}","kind":"{{Kind}}","confirmed":{{(IsConfirmed ? "true" : "false")}},"worked":{{(CountsAsWorkingDay ? "true" : "false")}}}""";
}

/// <summary>
/// Corrects a recorded holiday — most often the act of confirming a lunar date the decree has now fixed,
/// which is why <paramref name="IsConfirmed"/> is editable rather than write-once.
/// </summary>
/// <param name="CountsAsWorkingDay">
/// ⚠ <b>Nullable, and null means « leave it as it is » — not <c>false</c>.</b> This is a full-replace PUT
/// and the client lives in another repository, so a plain <c>bool</c> could not tell a client asking for
/// « chômé » from one that has never heard of the field: saving a holiday's <i>name</i> from an older
/// screen would silently undo a flag somebody deliberately set, and nothing anywhere would say so. The
/// other flag on this command, <paramref name="IsConfirmed"/>, is not nullable because every client that
/// exists already sends it.
/// </param>
public sealed record UpdateHolidayCommand(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed,
    bool? CountsAsWorkingDay = null) : ICommand<UpdateHolidayResult>, IAuditableCommand
{
    public string AuditAction => "HOLIDAY_UPDATED";
    public string AuditEntityType => "Holiday";
    public string? AuditEntityId => Id.ToString();
    public string? AuditMetadata =>
        $$"""{"name":"{{Name}}","from":"{{StartDate:yyyy-MM-dd}}","to":"{{EndDate:yyyy-MM-dd}}","confirmed":{{(IsConfirmed ? "true" : "false")}},"worked":{{(CountsAsWorkingDay is null ? "\"unchanged\"" : CountsAsWorkingDay.Value ? "true" : "false")}}}""";
}

/// <summary>
/// What the correction cost, in the same terms as <see cref="DeleteHolidayResult"/> — moving a holiday
/// off a date is the same event as removing it from there, and this is the path that actually happens:
/// the estimate entered in September is corrected the day the decree names Aïd.
/// </summary>
/// <param name="DatesMoved">
/// False when only the name, kind or <c>IsConfirmed</c> flag changed. Ticking « Date confirmée » on a
/// span that was already right costs nothing — no window's day count changes — and reporting slots then
/// would train the user to dismiss the one report that matters.
/// </param>
/// <param name="CountingChanged">
/// True when <c>CountsAsWorkingDay</c> was toggled. ⚠ <b>Reported apart from
/// <paramref name="DatesMoved"/> because it is a second way to change what every window over the date is
/// worth, arrived at without touching a date</b> — flagging the Fête du Trône worked gives a day back to
/// every créneau crossing it, exactly as deleting the row would. The two are named separately because the
/// sentences differ: one says the holiday moved, the other that it stopped costing anything.
/// </param>
/// <param name="SlotsSpanning">
/// Slots overlapping the span it <b>left</b> or the span it <b>arrived at</b>, counted once. Both halves
/// are affected and for opposite reasons: the first was laid around a holiday that is no longer there,
/// the second now contains one it never counted. Zero when neither <paramref name="DatesMoved"/> nor
/// <paramref name="CountingChanged"/> is true.
/// </param>
public sealed record UpdateHolidayResult(
    string Name,
    DateOnly StartDate,
    bool DatesMoved,
    bool CountingChanged,
    int SlotsSpanning);

/// <summary>
/// Removes a holiday. Nothing references the row, so this breaks no link — but any <c>StageSlot</c> whose
/// window was laid <em>over</em> it keeps the dates it was given, so the count it was generated from no
/// longer reproduces. <see cref="DeleteHolidayResult.SlotsSpanning"/> says how many, which is the number
/// worth seeing before confirming.
/// </summary>
public sealed record DeleteHolidayCommand(int Id) : ICommand<DeleteHolidayResult>, IAuditableCommand
{
    public string AuditAction => "HOLIDAY_DELETED";
    public string AuditEntityType => "Holiday";
    public string? AuditEntityId => Id.ToString();
    public string? AuditMetadata => null;
}

public sealed record DeleteHolidayResult(string Name, DateOnly StartDate, int SlotsSpanning);

internal sealed class CreateHolidayCommandValidator : AbstractValidator<CreateHolidayCommand>
{
    public CreateHolidayCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("Un jour férié se termine avant de commencer.");
    }
}

internal sealed class UpdateHolidayCommandValidator : AbstractValidator<UpdateHolidayCommand>
{
    public UpdateHolidayCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("Un jour férié se termine avant de commencer.");
    }
}

internal sealed class CreateHolidayCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateHolidayCommand, int>
{
    public async Task<Result<int>> Handle(CreateHolidayCommand request, CancellationToken cancellationToken)
    {
        bool exists = await dbContext.Holidays.AnyAsync(
            h => h.StartDate == request.StartDate && h.Name == request.Name, cancellationToken);

        if (exists)
            return Result.Failure<int>(HolidayErrors.Duplicate(request.StartDate, request.Name));

        var holiday = new Holiday
        {
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            IsConfirmed = request.IsConfirmed,
            CountsAsWorkingDay = request.CountsAsWorkingDay,
        };

        dbContext.Holidays.Add(holiday);
        await dbContext.SaveChangesAsync(cancellationToken);

        return holiday.Id;
    }
}

internal sealed class UpdateHolidayCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateHolidayCommand, UpdateHolidayResult>
{
    public async Task<Result<UpdateHolidayResult>> Handle(
        UpdateHolidayCommand request, CancellationToken cancellationToken)
    {
        var holiday = await dbContext.Holidays
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);

        if (holiday is null)
            return Result.Failure<UpdateHolidayResult>(HolidayErrors.NotFound(request.Id));

        bool clash = await dbContext.Holidays.AnyAsync(
            h => h.Id != request.Id && h.StartDate == request.StartDate && h.Name == request.Name,
            cancellationToken);

        if (clash)
            return Result.Failure<UpdateHolidayResult>(
                HolidayErrors.Duplicate(request.StartDate, request.Name));

        bool datesMoved = holiday.StartDate != request.StartDate || holiday.EndDate != request.EndDate;
        // Null is « unchanged », never « chômé » — see the command's own note.
        bool worked = request.CountsAsWorkingDay ?? holiday.CountsAsWorkingDay;
        bool countingChanged = holiday.CountsAsWorkingDay != worked;

        // Counted before the write, and over the union of the old and the new span: a slot laid around
        // the old date no longer reproduces from the count that produced it, and one covering the new
        // date has just gained a non-working stretch it never counted. Overlapping spans — the usual
        // case, a date corrected by a day — are counted once, which is what the confirmation says.
        //
        // ⚠ Toggling « travaillé » opens the same gate without moving a date: the span stays where it is
        // and every window over it changes length. Gating on datesMoved alone would have made the one
        // change that gives days *back* the only silent one.
        int slotsSpanning = datesMoved || countingChanged
            ? await dbContext.StageSlots.CountAsync(
                s => (s.StartDate <= holiday.EndDate && s.EndDate >= holiday.StartDate)
                  || (s.StartDate <= request.EndDate && s.EndDate >= request.StartDate),
                cancellationToken)
            : 0;

        holiday.StartDate = request.StartDate;
        holiday.EndDate = request.EndDate;
        holiday.Name = request.Name.Trim();
        holiday.Kind = request.Kind;
        holiday.IsConfirmed = request.IsConfirmed;
        holiday.CountsAsWorkingDay = worked;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateHolidayResult(
            holiday.Name, holiday.StartDate, datesMoved, countingChanged, slotsSpanning);
    }
}

internal sealed class DeleteHolidayCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteHolidayCommand, DeleteHolidayResult>
{
    public async Task<Result<DeleteHolidayResult>> Handle(
        DeleteHolidayCommand request, CancellationToken cancellationToken)
    {
        var holiday = await dbContext.Holidays
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);

        if (holiday is null)
            return Result.Failure<DeleteHolidayResult>(HolidayErrors.NotFound(request.Id));

        int slotsSpanning = await dbContext.StageSlots.CountAsync(
            s => s.StartDate <= holiday.EndDate && s.EndDate >= holiday.StartDate, cancellationToken);

        dbContext.Holidays.Remove(holiday);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteHolidayResult(holiday.Name, holiday.StartDate, slotsSpanning);
    }
}
