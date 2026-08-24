using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Outcome;

/// <summary>
/// Takes a verdict back and puts the year in progress again.
/// </summary>
/// <remarks>
/// ⚠ The response says whether the following year's registration already exists, because this command
/// deliberately does not touch it: that row may already carry a group, cohorts and published périodes,
/// and cascading a correction into it would delete a student's rotations. Withdrawing the verdict and
/// deleting what it produced are two decisions, and only the first one is safe to make automatically.
/// </remarks>
public sealed record ReopenRegistrationYearCommand(
    Guid RegistrationId,
    string? Reason = null) : ICommand<ReopenYearReport>, IAuditableCommand
{
    public string AuditAction => "YEAR_OUTCOME_REOPENED";
    public string AuditEntityType => "Registration";
    public string? AuditEntityId => RegistrationId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new { reason = Reason });
}

/// <param name="LaterRegistrationExists">
/// A registration in a later year already exists for this student — almost always one the réinscription
/// created from the verdict just withdrawn. Reported, never removed.
/// </param>
public sealed record ReopenYearReport(
    RegistrationStatus WithdrawnOutcome,
    bool LaterRegistrationExists);

internal sealed class ReopenRegistrationYearCommandValidator
    : AbstractValidator<ReopenRegistrationYearCommand>
{
    public ReopenRegistrationYearCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ReopenRegistrationYearCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ReopenRegistrationYearCommand, ReopenYearReport>
{
    public async Task<Result<ReopenYearReport>> Handle(
        ReopenRegistrationYearCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.OutcomeNotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReopenYearReport>(access.Error);

        var registration = await dbContext.Registrations
            .Include(r => r.AcademicYear)
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
            return Result.Failure<ReopenYearReport>(RegistrationErrors.NotFound(request.RegistrationId));

        var withdrawn = registration.Status;

        var result = registration.ReopenYear(request.Reason);
        if (result.IsFailure)
            return Result.Failure<ReopenYearReport>(result.Error);

        // By start date, not by id: years are created in whatever order somebody typed them, and it is
        // the calendar that says which one comes after this registration's.
        bool laterExists = await dbContext.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.StudentId == registration.StudentId
                        && r.Id != registration.Id
                        && r.AcademicYear.StartDate > registration.AcademicYear.StartDate,
                cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReopenYearReport(withdrawn, laterExists);
    }
}
