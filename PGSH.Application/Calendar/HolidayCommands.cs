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
public sealed record CreateHolidayCommand(
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed = true) : ICommand<int>, IAuditableCommand
{
    public string AuditAction => "HOLIDAY_CREATED";
    public string AuditEntityType => "Holiday";
    public string? AuditEntityId => null;
    public string? AuditMetadata =>
        $$"""{"name":"{{Name}}","from":"{{StartDate:yyyy-MM-dd}}","to":"{{EndDate:yyyy-MM-dd}}","kind":"{{Kind}}","confirmed":{{(IsConfirmed ? "true" : "false")}}}""";
}

/// <summary>
/// Corrects a recorded holiday — most often the act of confirming a lunar date the decree has now fixed,
/// which is why <paramref name="IsConfirmed"/> is editable rather than write-once.
/// </summary>
public sealed record UpdateHolidayCommand(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    string Name,
    HolidayKind Kind,
    bool IsConfirmed) : ICommand, IAuditableCommand
{
    public string AuditAction => "HOLIDAY_UPDATED";
    public string AuditEntityType => "Holiday";
    public string? AuditEntityId => Id.ToString();
    public string? AuditMetadata =>
        $$"""{"name":"{{Name}}","from":"{{StartDate:yyyy-MM-dd}}","to":"{{EndDate:yyyy-MM-dd}}","confirmed":{{(IsConfirmed ? "true" : "false")}}}""";
}

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
        };

        dbContext.Holidays.Add(holiday);
        await dbContext.SaveChangesAsync(cancellationToken);

        return holiday.Id;
    }
}

internal sealed class UpdateHolidayCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateHolidayCommand>
{
    public async Task<Result> Handle(UpdateHolidayCommand request, CancellationToken cancellationToken)
    {
        var holiday = await dbContext.Holidays
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken);

        if (holiday is null)
            return Result.Failure(HolidayErrors.NotFound(request.Id));

        bool clash = await dbContext.Holidays.AnyAsync(
            h => h.Id != request.Id && h.StartDate == request.StartDate && h.Name == request.Name,
            cancellationToken);

        if (clash)
            return Result.Failure(HolidayErrors.Duplicate(request.StartDate, request.Name));

        holiday.StartDate = request.StartDate;
        holiday.EndDate = request.EndDate;
        holiday.Name = request.Name.Trim();
        holiday.Kind = request.Kind;
        holiday.IsConfirmed = request.IsConfirmed;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
