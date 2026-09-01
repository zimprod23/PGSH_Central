using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.AcademicYears.Manage;

/// <summary>
/// Corrects a year's label or its span. The ordinary case is a year created with a placeholder span
/// and dated properly once the calendar is settled.
/// </summary>
/// <remarks>
/// ⚠ <b>Moving the span does not move what was laid on it.</b> A <c>StageSlot</c> keeps the dates it
/// was authored with, so narrowing a year can leave its own périodes outside it — which is not wrong
/// enough to refuse (a year is routinely corrected while its axis is being drafted) and far too easy
/// to do by accident. <see cref="UpdatedAcademicYearReport.SlotsOutsideSpan"/> says how many, before
/// the write, so the confirmation can name the number. Same shape as <c>UpdateHolidayCommand</c>'s
/// <c>SlotsSpanning</c>, and for the same reason.
/// </remarks>
public sealed record UpdateAcademicYearCommand(
    int AcademicYearId,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand<UpdatedAcademicYearReport>, IAuditableCommand
{
    public string AuditAction => "ACADEMIC_YEAR_UPDATED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(
        new { label = Label, startDate = StartDate, endDate = EndDate });
}

public sealed record UpdatedAcademicYearReport(
    int AcademicYearId,
    string Label,
    int SlotsOutsideSpan);

internal sealed class UpdateAcademicYearCommandValidator : AbstractValidator<UpdateAcademicYearCommand>
{
    public UpdateAcademicYearCommandValidator()
    {
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(50);
    }
}

internal sealed class UpdateAcademicYearCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearCalendarGuard calendarGuard,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<UpdateAcademicYearCommand, UpdatedAcademicYearReport>
{
    public async Task<Result<UpdatedAcademicYearReport>> Handle(
        UpdateAcademicYearCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AcademicYearErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<UpdatedAcademicYearReport>(access.Error);

        var year = await dbContext.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, cancellationToken);

        if (year is null)
            return Result.Failure<UpdatedAcademicYearReport>(
                AcademicYearErrors.NotFound(request.AcademicYearId));

        var free = await calendarGuard.EnsureFreeAsync(
            request.Label, request.StartDate, request.EndDate, year.Id, cancellationToken);

        if (free.IsFailure)
            return Result.Failure<UpdatedAcademicYearReport>(free.Error);

        // Counted before the write, against the span being moved to: afterwards the slots that fell
        // out look exactly like slots that were always somewhere else.
        int slotsOutsideSpan = await dbContext.StageSlots
            .AsNoTracking()
            .CountAsync(
                s => s.AcademicYearId == year.Id
                  && (s.StartDate < request.StartDate || s.EndDate > request.EndDate),
                cancellationToken);

        var renamed = year.Rename(request.Label);
        if (renamed.IsFailure)
            return Result.Failure<UpdatedAcademicYearReport>(renamed.Error);

        var rescheduled = year.Reschedule(request.StartDate, request.EndDate);
        if (rescheduled.IsFailure)
            return Result.Failure<UpdatedAcademicYearReport>(rescheduled.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdatedAcademicYearReport(year.Id, year.Label, slotsOutsideSpan);
    }
}
