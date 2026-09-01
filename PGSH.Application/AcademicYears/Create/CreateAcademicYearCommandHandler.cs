using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears.Manage;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears.Create;

internal sealed class CreateAcademicYearCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearCalendarGuard calendarGuard,
    CurrentYearDesignation designation,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CreateAcademicYearCommand, int>
{
    public async Task<Result<int>> Handle(CreateAcademicYearCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(AcademicYearErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<int>(access.Error);

        // Label uniqueness was checked here before; the overlap was not, and it is the half that
        // matters — a service's load is measured on dates, so two years sharing a day count every
        // slot in the overlap twice. Both rules now live in one place, shared with the update.
        var free = await calendarGuard.EnsureFreeAsync(
            request.Label, request.StartDate, request.EndDate, excludingId: null, ct);

        if (free.IsFailure)
            return Result.Failure<int>(free.Error);

        var year = new AcademicYear
        {
            Label = request.Label.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
        };

        dbContext.AcademicYears.Add(year);
        await dbContext.SaveChangesAsync(ct);

        // ⚠ Inserted first, then promoted — never the other way round. There is no transaction around
        // the pair (nothing in this codebase has one, and the in-memory provider cannot honour it), so
        // the order decides what a failure costs: demote-then-insert leaves the base with *no* current
        // year if the insert fails, and every handler that omits a year then has nothing to resolve.
        // Insert-then-promote can only fail with the new year present and the old one still current,
        // which is a state somebody can see and correct.
        if (request.IsCurrent)
        {
            var change = await designation.PromoteAsync(year, ct);
            if (change.IsFailure)
                return Result.Failure<int>(change.Error);
        }

        return year.Id;
    }
}
