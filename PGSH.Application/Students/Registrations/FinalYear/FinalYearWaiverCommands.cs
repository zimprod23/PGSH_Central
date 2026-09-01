using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.FinalYear;

/// <summary>
/// Lets one named student start his final year despite an unvalidated earlier stage.
///
/// <para>The exception the faculty has always granted, made recordable. What it costs to *not* have
/// this is not that the rule gets broken — it is that it gets broken in SQL, by somebody who leaves
/// no reason behind.</para>
/// </summary>
/// <param name="AcademicYearId">The year he is being allowed to start his final year in.</param>
public sealed record GrantFinalYearWaiverCommand(
    Guid   StudentId,
    int    AcademicYearId,
    string Reason) : ICommand<Guid>, IAuditableCommand
{
    public string  AuditAction     => "FINAL_YEAR_WAIVER_GRANTED";
    public string  AuditEntityType => "Student";
    public string? AuditEntityId   => StudentId.ToString();
    public string? AuditMetadata   => $$"""{"academicYearId":{{AcademicYearId}}}""";
}

/// <summary>
/// Withdraws a waiver. Refused once it has been used — the registration it permitted exists, and
/// removing its justification would leave a student sitting in a year nothing explains.
/// </summary>
public sealed record RevokeFinalYearWaiverCommand(Guid Id) : ICommand, IAuditableCommand
{
    public string  AuditAction     => "FINAL_YEAR_WAIVER_REVOKED";
    public string  AuditEntityType => "FinalYearEntryWaiver";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => null;
}

internal sealed class GrantFinalYearWaiverCommandValidator : AbstractValidator<GrantFinalYearWaiverCommand>
{
    public GrantFinalYearWaiverCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class RevokeFinalYearWaiverCommandValidator : AbstractValidator<RevokeFinalYearWaiverCommand>
{
    public RevokeFinalYearWaiverCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class GrantFinalYearWaiverCommandHandler(
    IApplicationDbContext dbContext,
    OutstandingStageFinder finder,
    IUserContext userContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<GrantFinalYearWaiverCommand, Guid>
{
    public async Task<Result<Guid>> Handle(GrantFinalYearWaiverCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<Guid>(access.Error);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<Guid>(RegistrationErrors.WaiverReasonRequired);

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, ct))
            return Result.Failure<Guid>(StudentErrors.NotFound(request.StudentId));

        string? yearLabel = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == request.AcademicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(ct);

        if (yearLabel is null)
            return Result.Failure<Guid>(StageErrors.AcademicYearNotFound(request.AcademicYearId));

        bool exists = await dbContext.FinalYearEntryWaivers.AnyAsync(
            w => w.StudentId == request.StudentId && w.AcademicYearId == request.AcademicYearId, ct);

        if (exists)
            return Result.Failure<Guid>(
                RegistrationErrors.WaiverAlreadyGranted(request.StudentId, yearLabel));

        // ⚠ Refused when there is nothing to excuse. A waiver is evidence that an exception was made;
        // one granted against no debt would sit in the record asserting something that never happened,
        // and would silently pre-authorise a debt the student has not yet incurred.
        var owed = await finder.ForStudentAsync(request.StudentId, ct);
        if (owed.Count == 0)
            return Result.Failure<Guid>(RegistrationErrors.WaiverNotNeeded);

        var waiver = new FinalYearEntryWaiver
        {
            Id                 = Guid.NewGuid(),
            StudentId          = request.StudentId,
            AcademicYearId     = request.AcademicYearId,
            Reason             = request.Reason.Trim(),
            // Captured now: by the time anyone reads this back, the stage may have been revalidated,
            // dropped by a new CNPN, or served elsewhere — and a waiver that cannot say what it
            // excused is not a record.
            OutstandingAtGrant = owed.Count,
            OutstandingSummary = OutstandingStageFinder.Summarize(owed, max: 6),
            GrantedByUserId    = userContext.UserId == Guid.Empty ? null : userContext.UserId,
            GrantedOn          = DateTime.UtcNow,
        };

        dbContext.FinalYearEntryWaivers.Add(waiver);
        await dbContext.SaveChangesAsync(ct);
        return waiver.Id;
    }
}

internal sealed class RevokeFinalYearWaiverCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<RevokeFinalYearWaiverCommand>
{
    public async Task<Result> Handle(RevokeFinalYearWaiverCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return access;

        var waiver = await dbContext.FinalYearEntryWaivers
            .FirstOrDefaultAsync(w => w.Id == request.Id, ct);

        if (waiver is null)
            return Result.Failure(RegistrationErrors.WaiverNotFound(request.Id));

        // Once the registration it permitted exists, the waiver is its justification. Removing it
        // would leave a student sitting in a final year with an unvalidated stage and nothing on
        // record saying who allowed it — which is the exact state this feature exists to prevent.
        bool used = await dbContext.Registrations.AnyAsync(
            r => r.StudentId == waiver.StudentId && r.AcademicYearId == waiver.AcademicYearId, ct);

        if (used)
            return Result.Failure(RegistrationErrors.WaiverAlreadyUsed(waiver.Id));

        dbContext.FinalYearEntryWaivers.Remove(waiver);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }
}
